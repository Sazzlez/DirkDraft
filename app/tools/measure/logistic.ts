// Logistic regression for the measurement (docs/draft-konzept.md, Phase 2): does a feature predict
// the winner beyond what the champions' own strength already does? Newton–Raphson on the
// log-likelihood with an intercept; small models only (a few dozen columns), so the Hessian is
// inverted directly. A tiny ridge keeps a column that never varies from breaking the inversion.

export interface LogisticFit {
  /** Intercept first, then one per column of x. Log-odds. */
  coefficients: number[];
  /** Standard errors from the inverted Hessian, same order. */
  standardErrors: number[];
  iterations: number;
  converged: boolean;
}

const sigmoid = (z: number): number => 1 / (1 + Math.exp(-z));

/** The predicted probability of y = 1 for one row. */
export function predict(fit: LogisticFit, row: readonly number[], offset = 0): number {
  let z = offset + fit.coefficients[0]!;
  for (let j = 0; j < row.length; j++) z += fit.coefficients[j + 1]! * row[j]!;
  return sigmoid(z);
}

/** Fits y ~ 1 + x (+ offset with a fixed coefficient of 1, when given). */
export function fitLogistic(x: readonly (readonly number[])[], y: readonly number[], offset?: readonly number[], ridge = 1e-6, maxIterations = 50): LogisticFit {
  const n = x.length;
  const k = (x[0]?.length ?? 0) + 1;
  const beta = new Array<number>(k).fill(0);
  let hessian: number[][] = [];
  let iterations = 0;
  let converged = false;

  for (; iterations < maxIterations && !converged; iterations++) {
    const gradient = new Array<number>(k).fill(0);
    hessian = Array.from({ length: k }, () => new Array<number>(k).fill(0));
    for (let i = 0; i < n; i++) {
      const row = x[i]!;
      let z = (offset?.[i] ?? 0) + beta[0]!;
      for (let j = 1; j < k; j++) z += beta[j]! * row[j - 1]!;
      const p = sigmoid(z);
      const w = p * (1 - p);
      const residual = y[i]! - p;
      for (let a = 0; a < k; a++) {
        const xa = a === 0 ? 1 : row[a - 1]!;
        gradient[a]! += xa * residual;
        for (let b = a; b < k; b++) hessian[a]![b]! += w * xa * (b === 0 ? 1 : row[b - 1]!);
      }
    }
    for (let a = 0; a < k; a++) {
      for (let b = 0; b < a; b++) hessian[a]![b] = hessian[b]![a]!;
      hessian[a]![a]! += ridge;
      gradient[a]! -= ridge * beta[a]!;
    }
    const step = solve(hessian, gradient);
    let largest = 0;
    for (let j = 0; j < k; j++) {
      beta[j]! += step[j]!;
      largest = Math.max(largest, Math.abs(step[j]!));
    }
    converged = largest < 1e-8;
  }

  const inverse = invert(hessian);
  return {
    coefficients: beta,
    standardErrors: inverse.map((row, j) => Math.sqrt(Math.max(0, row[j]!))),
    iterations,
    converged,
  };
}

/** Mean negative log-likelihood per row, in nats; lower is better. */
export function logLoss(fit: LogisticFit, x: readonly (readonly number[])[], y: readonly number[], offset?: readonly number[]): number {
  let total = 0;
  for (let i = 0; i < x.length; i++) {
    const p = Math.min(1 - 1e-12, Math.max(1e-12, predict(fit, x[i]!, offset?.[i] ?? 0)));
    total -= y[i]! === 1 ? Math.log(p) : Math.log(1 - p);
  }
  return x.length > 0 ? total / x.length : 0;
}

/** Gauss–Jordan with partial pivoting: A⁻¹. */
export function invert(matrix: readonly (readonly number[])[]): number[][] {
  const n = matrix.length;
  const a = matrix.map((row, i) => [...row, ...Array.from({ length: n }, (_, j) => (i === j ? 1 : 0))]);
  for (let col = 0; col < n; col++) {
    let pivot = col;
    for (let r = col + 1; r < n; r++) if (Math.abs(a[r]![col]!) > Math.abs(a[pivot]![col]!)) pivot = r;
    if (Math.abs(a[pivot]![col]!) < 1e-300) throw new Error('Matrix nicht invertierbar');
    [a[col], a[pivot]] = [a[pivot]!, a[col]!];
    const lead = a[col]![col]!;
    for (let c = 0; c < 2 * n; c++) a[col]![c]! /= lead;
    for (let r = 0; r < n; r++) {
      if (r === col) continue;
      const factor = a[r]![col]!;
      if (factor === 0) continue;
      for (let c = 0; c < 2 * n; c++) a[r]![c]! -= factor * a[col]![c]!;
    }
  }
  return a.map((row) => row.slice(n));
}

function solve(matrix: readonly (readonly number[])[], vector: readonly number[]): number[] {
  const inverse = invert(matrix);
  return inverse.map((row) => row.reduce((sum, value, j) => sum + value * vector[j]!, 0));
}
