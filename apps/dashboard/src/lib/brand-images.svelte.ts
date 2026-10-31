import type { BrandImageKind, BrandingImage } from '@screenhire/api-client';
import { createCreateBrandImageUpload, createGetBrandImage, createVerifyBrandImageUpload } from '@screenhire/api-client/svelte-query';
import { describeError } from '#lib/query.js';

/**
 * One brand image slot on Settings, Branding (docs/architecture/branding.md): the image in it and its upload. An upload
 * goes straight to storage through the API's create-only link, as a candidate's resume does; the API then checks the
 * bytes and queues the malware scan, and the slot asks again each second while the scan runs. Nothing reaches the
 * careers pages until the page's Save. Make one in a component's script: it holds queries.
 */
export class BrandImageSlot {
  image = $state<BrandingImage | null>(null);
  uploading = $state(false);
  failure = $state('');

  readonly #kind: BrandImageKind;
  readonly #noun: string;
  readonly #upload = createCreateBrandImageUpload();
  readonly #verify = createVerifyBrandImageUpload();

  constructor(kind: BrandImageKind, noun: string) {
    this.#kind = kind;
    this.#noun = noun;
    const progress = createGetBrandImage(
      () => this.image?.id ?? '',
      () => ({ query: { enabled: this.scanning, refetchInterval: 1000 } }),
    );
    $effect(() => {
      const latest = progress.data?.data;
      if (latest && this.image && latest.id === this.image.id && latest.scanStatus !== this.image.scanStatus) this.image = latest;
    });
  }

  /** The malware scan has not answered yet. */
  get scanning(): boolean {
    return this.image?.scanStatus === 'Pending';
  }

  async choose(file: File): Promise<void> {
    this.failure = '';
    this.uploading = true;
    try {
      const ticket = (await this.#upload.mutateAsync({ data: { kind: this.#kind, fileName: file.name, sizeBytes: file.size } })).data;
      // Straight to storage, not an API route: the link is the credential, and the bytes never pass through the API.
      const sent = await fetch(ticket.uploadUrl, { method: ticket.method, headers: ticket.headers, body: file });
      if (!sent.ok) throw new Error(`Storage answered ${sent.status}`);
      this.image = (await this.#verify.mutateAsync({ imageId: ticket.uploadId })).data;
    } catch (error) {
      this.failure = describeError(error, `Could not upload the ${this.#noun}`);
    } finally {
      this.uploading = false;
    }
  }

  remove(): void {
    this.image = null;
    this.failure = '';
  }
}
