// Test stub for react-native. Only `Alert` is reached by the code under test;
// it records calls so a test can assert on the title AND the body a user would
// actually be shown.
const calls = [];

export const Alert = {
  alert(title, message) { calls.push({ title, message }); },
};

export function __alerts() { return calls.slice(); }
export function __resetAlerts() { calls.length = 0; }

export default { Alert };
