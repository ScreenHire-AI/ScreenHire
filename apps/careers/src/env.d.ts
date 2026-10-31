declare namespace App {
  interface Locals {
    /**
     * The sites allowed to frame this page and the one it is embedded in (lib/embed.ts). Set by pages that know their
     * organization; the middleware builds `frame-ancestors` from it, and `'self'` alone without it.
     */
    embedding?: import('./lib/embed').Embedding;
  }
}
