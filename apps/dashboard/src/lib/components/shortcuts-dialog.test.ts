import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { user, userAs } from '../../test-support/fixtures.js';
import { preferences } from '../preferences.svelte.js';
import { sections, sectionsFor } from '../shell.js';
import { shortcutGroups } from '../shortcuts.js';
import ShortcutsDialog from './shortcuts-dialog.svelte';

afterEach(() => {
  preferences.setShortcutsEnabled(true);
});

const pressQuestionMark = (target: Element = document.body) => fireEvent.keyDown(target, { key: '?', shiftKey: true });

describe('shortcuts dialog', () => {
  it('opens on ? and lists every shortcut in the catalogue', async () => {
    render(ShortcutsDialog, { props: { user } });
    expect(screen.queryByRole('dialog')).toBeNull();

    await pressQuestionMark();

    const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
    const listed = within(dialog).getAllByRole('term').map((term) => term.textContent?.trim());
    expect(listed).toEqual(shortcutGroups(sectionsFor(user)).flatMap((group) => group.shortcuts.map((shortcut) => shortcut.label)));
    const keys = within(dialog).getByText('Go to Candidates').nextElementSibling!;
    expect([...keys.querySelectorAll('kbd')].map((key) => key.textContent)).toEqual(['G', 'C']);
    expect(keys).toHaveTextContent('then');
    expect(dialog.querySelectorAll('kbd[data-slot="kbd"]').length).toBeGreaterThan(10);
    await expectNoAxeViolations(document.body);
  });

  it('holds the single-key switch, and ? stays closed once single-key shortcuts are off', async () => {
    render(ShortcutsDialog, { props: { open: true } });

    const toggle = await screen.findByRole('switch', { name: 'Single-key shortcuts' });
    expect(toggle).toBeChecked();
    // bits-ui says the state in data-state; the switch's fill must follow that attribute, or it draws empty.
    expect(toggle).toHaveAttribute('data-state', 'checked');
    expect(toggle.className).toContain('data-[state=checked]:bg-primary');
    await fireEvent.click(toggle);
    expect(preferences.shortcutsEnabled).toBe(false);
    expect(screen.getByText('Go to Jobs')).toHaveTextContent('Go to Jobs, off');
    expect(screen.getByText('Open the command palette')).not.toHaveTextContent('off');
  });

  it('does not open from ? while single-key shortcuts are off', async () => {
    preferences.setShortcutsEnabled(false);
    render(ShortcutsDialog);

    await pressQuestionMark();

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('lists Go to Verification, G then V, for a role that may open it, and not for one that may not', async () => {
    const { unmount } = render(ShortcutsDialog, { props: { user: userAs('Recruiter') } });
    await pressQuestionMark();
    const recruiterDialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
    expect(within(recruiterDialog).queryByText('Go to Verification')).toBeNull();
    expect(within(recruiterDialog).getByText('Go to Reports')).toBeInTheDocument();
    unmount();

    render(ShortcutsDialog, { props: { user } });
    await pressQuestionMark();
    const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
    const keys = within(dialog).getByText('Go to Verification').nextElementSibling!;
    expect([...keys.querySelectorAll('kbd[data-slot="kbd"]')].map((key) => key.textContent)).toEqual(['G', 'V']);
  });

  it('leaves ? to a text field', async () => {
    const field = document.createElement('input');
    document.body.append(field);
    render(ShortcutsDialog);

    await pressQuestionMark(field);

    expect(screen.queryByRole('dialog')).toBeNull();
    field.remove();
  });
});

describe('shortcut catalogue', () => {
  it('has a go-to chord for every sidebar section, and marks the single-key shortcuts', () => {
    const anywhere = shortcutGroups(sections, false)[0]!;
    for (const section of sections) {
      expect(anywhere.shortcuts).toContainEqual({
        label: `Go to ${section.label}`,
        keys: ['G', section.key.toUpperCase()],
        sequence: true,
        singleKey: true,
      });
    }
    expect(anywhere.shortcuts[0]).toEqual({ label: 'Open the command palette', keys: ['Ctrl', 'K'], singleKey: false });
    expect(shortcutGroups(sections, true)[0]!.shortcuts[0]!.keys).toEqual(['⌘', 'K']);
  });

  it('keeps G then S for Saved candidates, which has no sidebar item but is a choice on the Candidates page', () => {
    const saved = sections.find((section) => section.label === 'Saved candidates')!;

    expect(saved).toMatchObject({ href: '/candidates?list=saved', key: 's', sidebar: false });
    expect(shortcutGroups(sections, false)[0]!.shortcuts).toContainEqual({ label: 'Go to Saved candidates', keys: ['G', 'S'], sequence: true, singleKey: true });
  });
});
