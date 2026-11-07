<script lang="ts">
  import { DropArea } from '@screenhire/ui/questions';
  import * as Field from '#lib/components/ui/field/index.js';
  import Panel from '#lib/components/panel.svelte';

  // The drop area (design system section 11, A12): the first one is live, so a file dropped on it or chosen with it is
  // named; the second shows a chosen file; the third refuses a wrong type or a large file in words; the fourth is mid
  // upload; the last is the candidate pages' version of the same component.
  let live = $state<{ name: string; size: number } | null>(null);
  let liveRefused = $state('');
  let refused = $state('Upload a PDF or DOCX file.');
</script>

<Panel title="Drop area">
  <div class="flex flex-col gap-6">
    <Field.Field data-invalid={liveRefused ? true : undefined}>
      <Field.Label for="demo-drop-live">Resume</Field.Label>
      <DropArea
        tone="app"
        id="demo-drop-live"
        accept={['.pdf', '.docx']}
        maxBytes={10 * 1024 * 1024}
        invalid={!!liveRefused}
        describedby="demo-drop-live-hint"
        file={live}
        onfile={(file) => {
          live = { name: file.name, size: file.size };
          liveRefused = '';
        }}
        onproblem={(message) => (liveRefused = message)}
        onremove={() => {
          live = null;
          liveRefused = '';
        }}
      />
      <Field.Description id="demo-drop-live-hint">PDF or DOCX, up to 10 MB.</Field.Description>
      <Field.Error errors={liveRefused ? [{ message: liveRefused }] : []} />
    </Field.Field>
    <Field.Field>
      <Field.Label for="demo-drop-set">Chosen</Field.Label>
      <DropArea tone="app" id="demo-drop-set" file={{ name: 'jordan-lee-resume.pdf', size: 184_320 }} onfile={() => {}} onremove={() => {}} />
    </Field.Field>
    <Field.Field data-invalid={refused ? true : undefined}>
      <Field.Label for="demo-drop-refused">Refused</Field.Label>
      <DropArea
        tone="app"
        id="demo-drop-refused"
        accept={['.pdf', '.docx']}
        maxBytes={1024 * 1024}
        invalid={!!refused}
        file={null}
        onfile={() => (refused = '')}
        onproblem={(message) => (refused = message)}
      />
      <Field.Error errors={refused ? [{ message: refused }] : []} />
    </Field.Field>
    <Field.Field>
      <Field.Label for="demo-drop-busy">Uploading</Field.Label>
      <DropArea tone="app" id="demo-drop-busy" disabled status="Uploading..." onfile={() => {}} />
    </Field.Field>
    <Field.Field>
      <Field.Label for="demo-drop-candidate">On the careers pages</Field.Label>
      <div class="rounded-lg bg-candidate-background p-4 text-candidate-foreground">
        <DropArea id="demo-drop-candidate" accept={['.pdf']} onfile={() => {}} />
      </div>
    </Field.Field>
  </div>
</Panel>
