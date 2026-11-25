#!/usr/bin/env python3
"""
LyricPulse Offline GloVe 6B 100d -> LPQV int8 Quantized Embedding Builder.

Transforms Stanford GloVe 6B 100d text vectors into the compact binary LPQV format
consumed by LyricPulse.Core.Visuals.QuantizedEmbeddingTable & GloveSemanticVisualMatcher:

  1. Filters to the top-N alphabetic lowercase words (default: 50,000).
  2. Mean-centers all vectors across the retained vocabulary.
  3. Applies All-but-the-Top (Mu & Viswanath, ICLR 2018): removes projections onto the
     top-K principal components (default K=2) for isotropic cosine similarity.
  4. L2-normalizes each vector to unit length.
  5. Quantizes each vector to int8 [-127, 127] with a per-word float32 scale.
  6. Writes:
       - glove-100d-q8.bin  (LPQV header + [float32 scale + dim*int8] records, ~5.2 MB for 50k x 100d)
       - glove-vocab.txt    (UTF-8 lowercase word list aligned 1:1 with binary records)

Uses NumPy when installed for fast vectorized SVD; falls back cleanly to pure-Python
power-iteration PCA so the script remains runnable with zero third-party dependencies.

Usage:
    python tools/build_glove_index.py \
        --input glove.6B.100d.txt \
        --output-dir "%LOCALAPPDATA%/LyricPulse/visuals" \
        --max-vocab 50000 \
        --pca-remove 2
"""

from __future__ import annotations

import argparse
import math
import os
import random
import re
import struct
import sys
from pathlib import Path
from typing import List, Tuple

MAGIC = b"LPQV"
VERSION = 1
WORD_RE = re.compile(r"^[a-z]+(?:'[a-z]+)?$")


def load_glove_text(
    input_path: Path,
    max_vocab: int,
    expected_dim: int | None,
) -> Tuple[List[str], List[List[float]], int]:
    """Stream top-N clean lowercase alphabetic words from a GloVe text file."""
    words: List[str] = []
    vectors: List[List[float]] = []
    seen: set[str] = set()
    dim = expected_dim

    with input_path.open("r", encoding="utf-8", errors="ignore") as handle:
        for line in handle:
            parts = line.rstrip().split()
            if len(parts) < 3:
                continue

            word = parts[0].lower()
            if len(word) < 2 or len(word) > 40 or word in seen or not WORD_RE.match(word):
                continue

            vec_dim = len(parts) - 1
            if dim is None:
                dim = vec_dim
            elif vec_dim != dim:
                continue

            try:
                vec = [float(x) for x in parts[1:]]
            except ValueError:
                continue

            seen.add(word)
            words.append(word)
            vectors.append(vec)
            if len(words) >= max_vocab:
                break

    if dim is None or not words:
        raise ValueError(f"No valid word vectors were parsed from '{input_path}'.")

    return words, vectors, dim


def postprocess_all_but_the_top_numpy(vectors: List[List[float]], pca_remove: int) -> List[List[float]]:
    """Fast vectorized mean-centering, All-but-the-Top PCA projection removal, and L2 normalization."""
    import numpy as np  # type: ignore

    matrix = np.asarray(vectors, dtype=np.float32)
    matrix -= matrix.mean(axis=0, keepdims=True)

    k = min(pca_remove, matrix.shape[0] - 1, matrix.shape[1] - 1)
    if k > 0:
        # Covariance eigendecomposition (dim x dim) is much faster and lower memory than full N x dim SVD
        cov = (matrix.T @ matrix) / max(1, matrix.shape[0])
        eigenvalues, eigenvectors = np.linalg.eigh(cov.astype(np.float64))
        top_components = eigenvectors[:, -k:].astype(np.float32)
        projections = matrix @ top_components
        matrix -= projections @ top_components.T

    norms = np.linalg.norm(matrix, axis=1, keepdims=True)
    norms = np.where(norms > 1e-8, norms, 1.0)
    matrix /= norms
    return matrix.tolist()


def postprocess_all_but_the_top_pure_python(vectors: List[List[float]], dim: int, pca_remove: int) -> List[List[float]]:
    """Dependency-free mean-centering, power-iteration top-K PCA removal, and L2 normalization."""
    n = len(vectors)
    mean = [0.0] * dim
    for vec in vectors:
        for d in range(dim):
            mean[d] += vec[d]
    inv_n = 1.0 / n
    mean = [m * inv_n for m in mean]

    centered = [[vec[d] - mean[d] for d in range(dim)] for vec in vectors]

    # Power iteration on centered covariance matrix X^T X for top-K principal directions
    k = min(pca_remove, max(0, n - 1), max(0, dim - 1))
    rng = random.Random(1337)
    components: List[List[float]] = []

    for _ in range(k):
        u = [rng.uniform(-1.0, 1.0) for _ in range(dim)]
        u = _normalize(u)
        for _ in range(25):
            # v = X^T (X u)
            v = [0.0] * dim
            for row in centered:
                dot = sum(row[d] * u[d] for d in range(dim))
                for d in range(dim):
                    v[d] += dot * row[d]
            # Orthogonalize against previously found components
            for prev in components:
                proj = sum(v[d] * prev[d] for d in range(dim))
                for d in range(dim):
                    v[d] -= proj * prev[d]
            u = _normalize(v)
        components.append(u)

    if components:
        for row in centered:
            for comp in components:
                proj = sum(row[d] * comp[d] for d in range(dim))
                for d in range(dim):
                    row[d] -= proj * comp[d]

    return [_normalize(row) for row in centered]


def _normalize(vec: List[float]) -> List[float]:
    norm = math.sqrt(sum(x * x for x in vec))
    if norm <= 1e-8:
        return [0.0] * len(vec)
    inv = 1.0 / norm
    return [x * inv for x in vec]


def quantize_and_write_lpqv(
    words: List[str],
    unit_vectors: List[List[float]],
    dim: int,
    output_dir: Path,
) -> Tuple[Path, Path, float]:
    """Write LPQV binary + aligned UTF-8 vocab file and return (bin_path, vocab_path, max_cos_error)."""
    output_dir.mkdir(parents=True, exist_ok=True)
    bin_path = output_dir / "glove-100d-q8.bin"
    vocab_path = output_dir / "glove-vocab.txt"

    dequantized_sample: List[List[float]] = []
    sample_limit = min(len(words), 64)

    with bin_path.open("wb") as bin_file, vocab_path.open("w", encoding="utf-8", newline="\n") as vocab_file:
        bin_file.write(MAGIC)
        bin_file.write(struct.pack("<HHi", VERSION, dim, len(words)))

        for idx, (word, vec) in enumerate(zip(words, unit_vectors)):
            vocab_file.write(f"{word}\n")
            max_abs = max((abs(v) for v in vec), default=0.0)
            scale = (max_abs / 127.0) if max_abs > 0.0 else 0.0
            bin_file.write(struct.pack("<f", scale))

            if scale == 0.0:
                q_vals = [0] * dim
            else:
                inv_scale = 1.0 / scale
                q_vals = [max(-127, min(127, int(round(v * inv_scale)))) for v in vec]

            bin_file.write(struct.pack(f"<{dim}b", *q_vals))

            if idx < sample_limit:
                deq = _normalize([q * scale for q in q_vals])
                dequantized_sample.append(deq)

    # Measure maximum cosine quantization error on sample pairs
    max_err = 0.0
    for i in range(len(dequantized_sample)):
        for j in range(i + 1, len(dequantized_sample)):
            exact = sum(unit_vectors[i][d] * unit_vectors[j][d] for d in range(dim))
            approx = sum(dequantized_sample[i][d] * dequantized_sample[j][d] for d in range(dim))
            err = abs(exact - approx)
            if err > max_err:
                max_err = err

    return bin_path, vocab_path, max_err


def main(argv: List[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build LyricPulse LPQV int8 quantized GloVe embeddings.")
    parser.add_argument("--input", required=True, type=Path, help="Path to Stanford glove.6B.100d.txt")
    parser.add_argument("--output-dir", required=True, type=Path, help="Output directory for glove-100d-q8.bin and glove-vocab.txt")
    parser.add_argument("--max-vocab", type=int, default=50000, help="Maximum vocabulary words to retain (default: 50000)")
    parser.add_argument("--dim", type=int, default=None, help="Expected vector dimensionality (auto-detected if omitted)")
    parser.add_argument("--pca-remove", type=int, default=2, help="Top-K principal components to remove via All-but-the-Top (default: 2)")
    args = parser.parse_args(argv)

    if not args.input.is_file():
        print(f"Error: Input GloVe file not found: {args.input}", file=sys.stderr)
        return 1

    words, raw_vectors, dim = load_glove_text(args.input, args.max_vocab, args.dim)

    try:
        import numpy  # noqa: F401
        unit_vectors = postprocess_all_but_the_top_numpy(raw_vectors, args.pca_remove)
        backend = "numpy"
    except ImportError:
        unit_vectors = postprocess_all_but_the_top_pure_python(raw_vectors, dim, args.pca_remove)
        backend = "pure-python"

    bin_path, vocab_path, max_err = quantize_and_write_lpqv(words, unit_vectors, dim, args.output_dir)
    size_mb = os.path.getsize(bin_path) / (1024 * 1024)
    print(
        f"Built LPQV index ({backend}): vocab={len(words)}, dim={dim}, "
        f"size={size_mb:.2f} MB, max_sample_cos_err={max_err:.5f}"
    )
    print(f"  Binary: {bin_path}")
    print(f"  Vocab:  {vocab_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
