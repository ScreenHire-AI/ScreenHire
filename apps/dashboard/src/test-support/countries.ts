import { fireEvent, screen } from '@testing-library/svelte';

/**
 * Picks a country in a CountryPicker the way a person does with the keyboard: types its name, waits for it in the
 * list, presses Enter, which adds the first match, and waits for its pill.
 */
export async function pickCountry(field: HTMLElement, name: string): Promise<void> {
  await fireEvent.keyDown(field, { key: name.at(-1) });
  await fireEvent.input(field, { target: { value: name } });
  await screen.findByRole('option', { name });
  await fireEvent.keyDown(field, { key: 'Enter' });
  await screen.findByRole('button', { name: `Remove ${name}` });
}
