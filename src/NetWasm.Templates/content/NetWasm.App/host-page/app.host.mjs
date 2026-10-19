// Imports run in the page, so they can use the DOM directly.
export function show(message) {
  document.querySelector("#result").textContent = message;
}
