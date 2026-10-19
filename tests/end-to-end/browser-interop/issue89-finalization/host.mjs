export function register(callback) {
  globalThis.reproCallback = callback;
  globalThis.reproReady?.();
  return { dispose() { globalThis.reproCallback = null; } };
}
