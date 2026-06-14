// Typed error helpers. Each throws an AuthError carrying an HTTP status; the
// endpoint catches it and renders a JSON `{ error }` body with CORS applied.
// This keeps DB errors and stack traces from ever reaching the client.

export class AuthError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = "AuthError";
    this.status = status;
  }
}

export const bad = (msg: string) => new AuthError(400, msg);
export const unauthorized = (msg = "Unauthorized") => new AuthError(401, msg);
export const forbidden = (msg = "Forbidden") => new AuthError(403, msg);
export const notFound = (msg = "Not found") => new AuthError(404, msg);
export const conflict = (msg: string) => new AuthError(409, msg);
export const serverError = (msg = "Something went wrong. Please try again.") =>
  new AuthError(500, msg);
