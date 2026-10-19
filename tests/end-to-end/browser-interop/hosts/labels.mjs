// One provider module serves both hosts: the page binds it directly, and the
// worker binds it beside its notification channel.
export function createWorkerImports(notify) {
  return Object.freeze({
    label(value) {
      notify("labelled", [value]);
      return `#${value}`;
    },
  });
}
