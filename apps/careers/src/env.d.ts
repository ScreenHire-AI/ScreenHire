declare namespace App {
  interface Locals {
    /** The organization slug the request's host names, or null when the organization is in the path. */
    hostTenant: string | null;
  }
}
