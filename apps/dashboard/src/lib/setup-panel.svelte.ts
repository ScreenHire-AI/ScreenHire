/**
 * A request to open the notification bell at its setup section. Every page mounts its own top bar and bell, so the
 * request lives here: a page's "Open the setup checklist" button counts one up, and the bell on that page opens its
 * panel when the count passes the one it saw when it mounted.
 */
class SetupPanel {
  requests = $state(0);

  open(): void {
    this.requests += 1;
  }
}

export const setupPanel = new SetupPanel();
