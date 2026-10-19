export function createWorkerImports(notify) {
  return Object.freeze({ report(value) { notify("progress", [value]); } });
}
