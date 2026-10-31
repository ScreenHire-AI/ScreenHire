import type { PublicQuestion } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  emptyState,
  errorsFromProblem,
  fieldId,
  fields,
  NOTICE_VERSION,
  parseState,
  readAnswers,
  readDetails,
  readReview,
  stepsFor,
  toRequest,
  validateAnswers,
  validateDetails,
  validateReview,
} from './apply';

const question = (overrides: Partial<PublicQuestion> & Pick<PublicQuestion, 'id' | 'type'>): PublicQuestion => ({
  label: overrides.id,
  required: false,
  helpText: null,
  options: null,
  min: null,
  max: null,
  maxLength: null,
  acceptedFileTypes: null,
  ...overrides,
});

const resume = question({ id: 'resume', type: 'File', required: true, acceptedFileTypes: ['.pdf', '.docx'] });
const authorization = question({ id: 'work_authorization', type: 'YesNo', required: true });
const experience = question({
  id: 'experience',
  type: 'SingleChoice',
  required: true,
  options: [
    { id: 'junior', label: 'Junior' },
    { id: 'senior', label: 'Senior' },
  ],
});
const shifts = question({
  id: 'shifts',
  type: 'MultiChoice',
  options: [
    { id: 'mornings', label: 'Mornings' },
    { id: 'evenings', label: 'Evenings' },
  ],
});
const years = question({ id: 'years', type: 'Number', min: 0, max: 50 });
const portfolio = question({ id: 'portfolio', type: 'Url', maxLength: 2000 });
const questions = [resume, authorization, experience, shifts, years, portfolio];
const upload = { uploadId: '0199e8a0-0000-4000-8000-000000000001', fileName: 'cv.pdf', sizeBytes: 2048 };

const form = (entries: [string, string][]): FormData => {
  const data = new FormData();
  for (const [name, value] of entries) data.append(name, value);
  return data;
};

describe('steps', () => {
  it('skips the questions step when the job asks only for a resume', () => {
    expect(stepsFor([resume])).toEqual(['details', 'review']);
    expect(stepsFor(questions)).toEqual(['details', 'questions', 'review']);
  });
});

describe('parseState', () => {
  it('round-trips the state through the hidden field', () => {
    const state = { ...emptyState(), firstName: 'Casey', lastName: 'Morgan', answers: { shifts: ['mornings'] }, files: { resume: upload } };

    expect(parseState(JSON.stringify(state))).toEqual(state);
  });

  it('keeps the telemetry session only when it looks like a token the API issued', () => {
    expect(parseState(JSON.stringify({ telemetrySession: 'CfDJ8abc_-XYZ', deviceCheckConsent: true }))).toMatchObject({
      telemetrySession: 'CfDJ8abc_-XYZ',
      deviceCheckConsent: true,
    });
    expect(parseState(JSON.stringify({ telemetrySession: '<script>' })).telemetrySession).toBe('');
    expect(parseState(JSON.stringify({ telemetrySession: 'a'.repeat(1025) })).telemetrySession).toBe('');
    expect(parseState(JSON.stringify({ deviceCheckConsent: 'yes' })).deviceCheckConsent).toBe(false);
  });

  it('keeps a start time only when it is an ISO instant', () => {
    expect(parseState(JSON.stringify({ startedAt: '2026-10-20T09:00:00.000Z' })).startedAt).toBe('2026-10-20T09:00:00.000Z');
    expect(parseState(JSON.stringify({ startedAt: 'yesterday' })).startedAt).toBe('');
    expect(parseState(JSON.stringify({ startedAt: 1760950800000 })).startedAt).toBe('');
  });

  it('falls back to empty for anything malformed, and drops bad uploads', () => {
    expect(parseState('{not json')).toEqual(emptyState());
    expect(parseState(null)).toEqual(emptyState());
    const tampered = parseState(JSON.stringify({ firstName: 42, lastName: ['x'], aiUseDeclaration: 'Maybe', files: { resume: { uploadId: '../../x' } } }));
    expect(tampered).toEqual(emptyState());
  });
});

describe('reading a step', () => {
  it('reads details, trims them and reads the assistive technology switch', () => {
    const state = readDetails(form([['firstName', '  Casey '], ['lastName', ' Morgan '], ['email', 'casey@example.com'], ['accommodationRequested', 'on']]), emptyState());

    expect(state).toMatchObject({ firstName: 'Casey', lastName: 'Morgan', email: 'casey@example.com', phone: '', accommodationRequested: true });
  });

  it('reads the device-check tick', () => {
    expect(readDetails(form([['deviceCheckConsent', 'on']]), emptyState()).deviceCheckConsent).toBe(true);
    expect(readDetails(form([]), { ...emptyState(), deviceCheckConsent: true }).deviceCheckConsent).toBe(false);
  });

  it('reads "Keep my details for future roles" on the review step, unticked unless the candidate ticks it', () => {
    expect(emptyState().keepForFutureRoles).toBe(false);
    expect(readReview(form([['keepForFutureRoles', 'on']]), emptyState()).keepForFutureRoles).toBe(true);
    expect(readReview(form([]), { ...emptyState(), keepForFutureRoles: true }).keepForFutureRoles).toBe(false);
    expect(parseState(JSON.stringify({ keepForFutureRoles: true })).keepForFutureRoles).toBe(true);
    expect(parseState(JSON.stringify({ keepForFutureRoles: 'yes' })).keepForFutureRoles).toBe(false);
    expect(toRequest({ ...emptyState(), keepForFutureRoles: true }, [], { honeypotFilled: false, botCheckToken: null }).keepForFutureRoles).toBe(true);
  });

  it('reads answers, multiple choices and the upload the island left behind', () => {
    const state = readAnswers(
      form([
        [fields.answer('work_authorization'), 'no'],
        [fields.answer('shifts'), 'mornings'],
        [fields.answer('shifts'), 'evenings'],
        [fields.answer('portfolio'), '   '],
        [fields.upload('resume'), JSON.stringify(upload)],
      ]),
      questions,
      { ...emptyState(), answers: { portfolio: 'https://old.example.com' } },
    );

    expect(state.answers).toEqual({ work_authorization: 'no', shifts: ['mornings', 'evenings'] });
    expect(state.files.resume).toEqual(upload);
  });

  it('keeps AI-use details only with a yes', () => {
    expect(readReview(form([['aiUseDeclaration', 'Used'], ['aiUseDetails', 'Grammar']]), emptyState())).toMatchObject({
      aiUseDeclaration: 'Used',
      aiUseDetails: 'Grammar',
    });
    expect(readReview(form([['aiUseDeclaration', 'DidNotUse'], ['aiUseDetails', 'Grammar']]), emptyState()).aiUseDetails).toBe('');
    expect(readReview(form([['aiUseDeclaration', 'Sometimes']]), emptyState()).aiUseDeclaration).toBe('NotProvided');
  });
});

describe('validation', () => {
  it('asks for a first name, a real-looking email and the required resume, and lets the last name be left out', () => {
    expect(validateDetails({ ...emptyState(), email: 'not-an-email' }, questions)).toEqual({
      firstName: 'Enter your first name.',
      email: 'Enter an email address like name@example.com.',
      [fields.file('resume')]: 'Add a file.',
    });
    expect(validateDetails({ ...emptyState(), firstName: 'Casey', email: 'casey@example.com', files: { resume: upload } }, questions)).toEqual({});
  });

  it('accepts a knockout answer like any other', () => {
    const state = { ...emptyState(), answers: { work_authorization: 'no', experience: 'junior' } };

    expect(validateAnswers(state, [authorization, experience])).toEqual({});
  });

  it('reports answers of the wrong shape', () => {
    const state = {
      ...emptyState(),
      answers: { work_authorization: 'maybe', experience: 'principal', shifts: ['nights'], years: '51', portfolio: 'example.com' },
    };

    expect(Object.keys(validateAnswers(state, questions)).sort()).toEqual(
      ['q:experience', 'q:portfolio', 'q:shifts', 'q:work_authorization', 'q:years', 'file:resume'].sort(),
    );
  });

  it('takes numbers written the ways the API reads them', () => {
    for (const answer of ['5', '+5', '.5', '4.', '-0']) {
      expect(validateAnswers({ ...emptyState(), answers: { years: answer } }, [years])).toEqual({});
    }
    expect(validateAnswers({ ...emptyState(), answers: { years: 'five' } }, [years])).toHaveProperty('q:years');
  });

  it('requires an AI-use answer unless the job allows AI without asking', () => {
    expect(validateReview(emptyState(), 'AllowedWithDisclosure')).toHaveProperty('aiUseDeclaration');
    expect(validateReview(emptyState(), 'NotAllowed')).toHaveProperty('aiUseDeclaration');
    expect(validateReview(emptyState(), 'Allowed')).toEqual({});
  });
});

describe('toRequest', () => {
  it('sends answers as automation conditions read them, with the notice version and the checks', () => {
    const state = {
      ...emptyState(),
      firstName: 'Casey',
      lastName: 'Morgan',
      email: 'casey@example.com',
      accommodationRequested: true,
      answers: { work_authorization: 'yes', shifts: ['mornings'] },
      files: { resume: upload },
      aiUseDeclaration: 'Used' as const,
      aiUseDetails: 'Grammar',
      startedAt: '2026-10-20T09:00:00.000Z',
    };

    expect(toRequest(state, questions, { honeypotFilled: true, botCheckToken: null, userAgent: 'Mozilla/5.0 (Example)' })).toEqual({
      candidate: { firstName: 'Casey', lastName: 'Morgan', email: 'casey@example.com', phone: null },
      privacyNoticeVersion: NOTICE_VERSION,
      answers: [
        { questionId: 'resume', value: upload.uploadId },
        { questionId: 'work_authorization', value: 'yes' },
        { questionId: 'shifts', values: ['mornings'] },
      ],
      accommodationRequested: true,
      aiUseDeclaration: 'Used',
      aiUseDetails: 'Grammar',
      honeypotFilled: true,
      botCheckToken: null,
      userAgent: 'Mozilla/5.0 (Example)',
      formStartedAt: '2026-10-20T09:00:00.000Z',
      // With the assistive-technology switch on, nothing from the telemetry module goes with it.
      automationMarkers: [],
      deviceFingerprint: null,
      deviceCheckConsent: false,
      keepForFutureRoles: false,
      telemetrySessionToken: null,
      telemetry: null,
    });
  });

  it("sends the telemetry session and what the module left in the form", () => {
    const state = { ...emptyState('', 'CfDJ8token_-'), deviceCheckConsent: true };
    const snapshot = {
      sessionToken: 'CfDJ8token_-',
      pageViewId: '0199e8a0-0000-7000-8000-00000000aaaa',
      sequence: 2,
      fields: [],
      page: { visibilityChanges: 0, automationMarkers: [] },
    };

    const request = toRequest(state, [resume], {
      honeypotFilled: false,
      botCheckToken: null,
      telemetry: snapshot,
      deviceFingerprint: 'a'.repeat(64),
      automationMarkers: ['webdriver'],
    });

    expect(request).toMatchObject({
      telemetrySessionToken: 'CfDJ8token_-',
      telemetry: snapshot,
      deviceFingerprint: 'a'.repeat(64),
      deviceCheckConsent: true,
      automationMarkers: ['webdriver'],
    });
  });

  it('sends no session context it does not have', () => {
    const request = toRequest(emptyState(), [resume], { honeypotFilled: false, botCheckToken: null });

    expect(request.userAgent).toBeNull();
    expect(request.formStartedAt).toBeNull();
  });
});

describe('errorsFromProblem', () => {
  it("places the API's problems on their fields and lands on the earliest step", () => {
    const placed = errorsFromProblem(
      {
        'answers.experience': ['Choose one of the options.'],
        'answers.resume': ['This file is larger than 10 MB.'],
        aiUseDeclaration: ['Tell us.'],
        privacyNoticeVersion: ['Required.'],
      },
      questions,
    );

    expect(placed.step).toBe('details');
    expect(placed.errors).toEqual({
      'q:experience': 'Choose one of the options.',
      'file:resume': 'This file is larger than 10 MB.',
      aiUseDeclaration: 'Tell us.',
    });
    expect(placed.unplaced).toEqual(['Required.']);
  });
});

describe('fieldId', () => {
  it('makes element ids from field names', () => {
    expect(fieldId(fields.file('resume'))).toBe('field-file-resume');
    expect(fieldId('firstName')).toBe('field-firstName');
  });
});
