// See https://svelte.dev/docs/kit/types#app.d.ts
declare global {
  namespace App {
    interface PageState {
      /** What saving a job's details did, said once on the job page it returns to. */
      saved?: string;
    }
  }
}

export {};
