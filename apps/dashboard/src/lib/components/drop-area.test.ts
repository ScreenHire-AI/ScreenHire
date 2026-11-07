import { DropArea } from '@screenhire/ui/questions';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';

// The one component every file input is (design system A12): a real input inside a dashed area that also takes a drop.

afterEach(() => {
  vi.unstubAllGlobals();
});

const pdf = (name = 'resume.pdf', bytes = 2048) => new File([new Uint8Array(bytes)], name, { type: 'application/pdf' });
const files = (...dropped: File[]) => ({ dataTransfer: { files: dropped, types: ['Files'] } });

function renderArea(props: Record<string, unknown> = {}) {
  const onfile = vi.fn();
  const onproblem = vi.fn();
  const onremove = vi.fn();
  render(DropArea, { props: { id: 'resume', accept: ['.pdf', '.docx'], maxBytes: 10 * 1024 * 1024, tone: 'app', onfile, onproblem, onremove, ...props } });
  return {
    onfile,
    onproblem,
    onremove,
    area: () => document.querySelector<HTMLElement>('[data-drop-area]')!,
    input: () => document.getElementById('resume') as HTMLInputElement,
  };
}

describe('drop area', () => {
  it('keeps a real file input for the keyboard, screen readers and phones, and says what to do', () => {
    const { input } = renderArea();

    expect(input()).toHaveAttribute('type', 'file');
    expect(input()).toHaveAttribute('accept', '.pdf,.docx');
    expect(screen.getByText('Drop a file here or')).toBeInTheDocument();
    expect(screen.getByText('Choose file')).toBeInTheDocument();
  });

  it('highlights only while a file is over it, not text or a link, and stays on across the areas inside it', async () => {
    const { area } = renderArea();

    await fireEvent.dragEnter(area(), { dataTransfer: { types: ['text/plain'] } });
    expect(area()).not.toHaveAttribute('data-over');

    await fireEvent.dragEnter(area(), files());
    expect(area()).toHaveAttribute('data-over');
    // The pointer crosses into a child and out of it: the parent's leave must not clear the highlight.
    await fireEvent.dragEnter(area().firstElementChild!, files());
    await fireEvent.dragLeave(area());
    expect(area()).toHaveAttribute('data-over');
    await fireEvent.dragLeave(area());
    expect(area()).not.toHaveAttribute('data-over');
  });

  it('takes a dropped file, and only the first of several', async () => {
    const { area, onfile, onproblem } = renderArea();
    const first = pdf('first.pdf');

    await fireEvent.dragEnter(area(), files());
    await fireEvent.drop(area(), files(first, pdf('second.pdf')));

    expect(onfile).toHaveBeenCalledExactlyOnceWith(first);
    expect(onproblem).not.toHaveBeenCalled();
    expect(area()).not.toHaveAttribute('data-over');
  });

  it('puts a dropped file into its input when the field posts with its form', async () => {
    const { area, input } = renderArea({ name: 'file:resume' });
    const added: File[] = [];
    // jsdom has no DataTransfer; this stands in for the browser one.
    vi.stubGlobal(
      'DataTransfer',
      class {
        items = { add: (file: File) => added.push(file) };
        files = added;
      },
    );
    const assigned = vi.spyOn(input(), 'files', 'set').mockImplementation(() => {});

    await fireEvent.drop(area(), files(pdf()));

    expect(added).toHaveLength(1);
    expect(assigned).toHaveBeenCalledOnce();
  });

  it('refuses a wrong type and a file that is too large, picked or dropped, with the reason in words', async () => {
    const { area, input, onfile, onproblem } = renderArea({ maxBytes: 1024 });

    await fireEvent.drop(area(), files(new File(['x'], 'setup.exe')));
    await fireEvent.drop(area(), files(pdf('big.pdf', 2048)));
    await fireEvent.change(input(), { target: { files: [new File([], 'empty.pdf')] } });

    expect(onproblem.mock.calls.map(([message]) => message)).toEqual(['Upload a PDF or DOCX file.', 'This file is larger than 1 KB.', 'This file is empty.']);
    expect(onfile).not.toHaveBeenCalled();
  });

  it('takes a picked file the same way as a dropped one', async () => {
    const { input, onfile } = renderArea();
    const picked = pdf();

    await fireEvent.change(input(), { target: { files: [picked] } });

    expect(onfile).toHaveBeenCalledExactlyOnceWith(picked);
  });

  it('opens the picker from a click anywhere on the area, but not from Remove', async () => {
    const { area, input } = renderArea({ file: { name: 'resume.pdf', size: 2048 } });
    const open = vi.spyOn(input(), 'click');

    await fireEvent.click(area());
    expect(open).toHaveBeenCalledOnce();

    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(open).toHaveBeenCalledOnce();
  });

  it('names the chosen file and its size with Remove, and Remove tells the owner', async () => {
    const { onremove } = renderArea({ file: { name: 'resume.pdf', size: 184_320 } });

    expect(screen.getByText('resume.pdf')).toBeInTheDocument();
    expect(screen.getByText('180 KB')).toBeInTheDocument();
    expect(screen.getByText('Replace file')).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(onremove).toHaveBeenCalledOnce();
  });

  it('ignores a drop while it is busy, and says what it is doing', async () => {
    const { area, onfile } = renderArea({ disabled: true, status: 'Uploading...' });

    await fireEvent.drop(area(), files(pdf()));

    expect(onfile).not.toHaveBeenCalled();
    expect(screen.getByText('Uploading...')).toBeInTheDocument();
    expect(area()).toHaveAttribute('data-disabled');
  });

  it('has no accessibility violations with a file chosen and the field invalid', async () => {
    // The owning field supplies the label, as every field does.
    document.body.insertAdjacentHTML('afterbegin', '<label for="resume">Resume</label>');
    renderArea({ file: { name: 'resume.pdf', size: 2048 }, invalid: true });
    await expectNoAxeViolations();
  });
});
