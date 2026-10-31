import type { PublicQuestion } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  emptyState,
  errorsFromProblem,
  fieldId,
  fields,
  NOTICE_VERSION,
  pagesFor,
  parseState,
  postedQuestions,
  questionsOn,
  readAnswers,
  readDetails,
  readReview,
  recordTime,
  stepOf,
  stepsFor,
  toDraftRequest,
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
  it('skips the pages of questions when the job asks only for a resume', () => {
    expect(stepsFor({ questions: [resume] })).toEqual(['details', 'review']);
    expect(stepsFor({ questions, questionsPerPage: 8 })).toEqual(['details', 'page-1', 'review']);
  });

  it("splits the questions into pages of the organization's size, the resume on the details step", () => {
    const shape = { questions, questionsPerPage: 2 };

    expect(stepsFor(shape)).toEqual(['details', 'page-1', 'page-2', 'page-3', 'review']);
    expect(questionsOn('details', shape).map((q) => q.id)).toEqual(['resume']);
    expect(questionsOn('page-2', shape).map((q) => q.id)).toEqual(['shifts', 'years']);
    expect(pagesFor(shape).every((page) => page.name === null)).toBe(true);
    expect(stepOf('portfolio', shape)).toBe('page-3');
    expect(stepOf('resume', shape)).toBe('details');
  });

  it('shows one named section a page, whatever the page size', () => {
    const shape = {
      questions,
      questionsPerPage: 1,
      sections: [
        { id: 'eligibility', name: 'Eligibility', questionIds: ['work_authorization'] },
        { id: 'experience', name: 'Your experience', questionIds: ['experience', 'shifts', 'years', 'portfolio'] },
      ],
    };

    expect(pagesFor(shape).map((page) => [page.step, page.name, page.questions.length])).toEqual([
      ['page-1', 'Eligibility', 1],
      ['page-2', 'Your experience', 4],
    ]);
  });

  it('reads only the questions the posted page showed', () => {
    const posted = form([['pageQuestions', 'experience,unknown']]);

    expect(postedQuestions(posted, questions).map((q) => q.id)).toEqual(['experience']);
  });
});

describe('time on each page', () => {
  const shown = { ...emptyState(), pageShownAt: '2026-10-30T09:00:00.000Z' };

  it('adds the seconds since the page was shown to that page', () => {
    const once = recordTime(shown, 'page-1', new Date('2026-10-30T09:00:42.000Z'));
    const twice = recordTime({ ...once, pageShownAt: '2026-10-30T10:00:00.000Z' }, 'page-1', new Date('2026-10-30T10:00:08.000Z'));

    expect(once.pageSeconds).toEqual({ 'page-1': 42 });
    expect(twice.pageSeconds).toEqual({ 'page-1': 50 });
  });

  it('counts at most 30 minutes a page, nothing for a clock that went back, and nothing without a stamp', () => {
    expect(recordTime(shown, 'details', new Date('2026-10-31T09:00:00.000Z')).pageSeconds).toEqual({ details: 1800 });
    expect(recordTime(shown, 'details', new Date('2026-10-30T08:00:00.000Z')).pageSeconds).toEqual({ details: 0 });
    expect(recordTime(emptyState(), 'details', new Date()).pageSeconds).toEqual({});
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

  it('keeps the page stamp only when it is an ISO instant', () => {
    expect(parseState(JSON.stringify({ pageShownAt: '2026-10-20T09:00:00.000Z' })).pageShownAt).toBe('2026-10-20T09:00:00.000Z');
    expect(parseState(JSON.stringify({ pageShownAt: 'yesterday' })).pageShownAt).toBe('');
    expect(parseState(JSON.stringify({ pageShownAt: 1760950800000 })).pageShownAt).toBe('');
  });

  it('keeps page times, the way back to the review and the draft only in their shapes', () => {
    const parsed = parseState(
      JSON.stringify({
        pageSeconds: { details: 12, 'page-2': 40, 'page-x': 5, review: 1801, other: 3 },
        returnTo: 'review',
        draft: { token: 'CfDJ8draft_-', email: 'casey@example.com' },
      }),
    );

    expect(parsed.pageSeconds).toEqual({ details: 12, 'page-2': 40 });
    expect(parsed.returnTo).toBe('review');
    expect(parsed.draft).toEqual({ token: 'CfDJ8draft_-', email: 'casey@example.com' });
    expect(parseState(JSON.stringify({ returnTo: 'page-1', draft: { token: '<x>', email: 'a' } }))).toMatchObject({ returnTo: '', draft: null });
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
      pageSeconds: { details: 20, 'page-1': 45 },
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
      // With the assistive-technology switch on, no time on the pages and nothing from the telemetry module goes with it.
      pageTimes: null,
      automationMarkers: [],
      deviceFingerprint: null,
      deviceCheckConsent: false,
      keepForFutureRoles: false,
      telemetrySessionToken: null,
      telemetry: null,
      // Applied on the careers site itself, not inside another site.
      embedOrigin: null,
    });
  });

  it("sends the telemetry session and what the module left in the form", () => {
    const state = { ...emptyState('CfDJ8token_-'), deviceCheckConsent: true, pageSeconds: { details: 20, 'page-1': 45 } };
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
      pageTimes: [
        { page: 'details', seconds: 20 },
        { page: 'page-1', seconds: 45 },
      ],
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
    expect(request.pageTimes).toEqual([]);
  });
});

describe('toDraftRequest', () => {
  it('keeps the details and the non-file answers, never files, the session or the time on the pages', () => {
    const state = {
      ...emptyState('CfDJ8token_-'),
      firstName: 'Casey',
      email: 'casey@example.com',
      answers: { work_authorization: 'no', shifts: ['evenings'] },
      files: { resume: upload },
      pageSeconds: { details: 30 },
      deviceCheckConsent: true,
    };

    expect(toDraftRequest(state, questions)).toEqual({
      candidate: { firstName: 'Casey', lastName: null, email: 'casey@example.com', phone: null },
      answers: [
        { questionId: 'work_authorization', value: 'no' },
        { questionId: 'shifts', values: ['evenings'] },
      ],
      accommodationRequested: false,
      aiUseDeclaration: 'NotProvided',
      aiUseDetails: null,
    });
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

  it('lands on the page that asks the first question with a problem', () => {
    const placed = errorsFromProblem({ 'answers.portfolio': ['Enter a full web address.'], 'answers.shifts': ['Choose.'] }, questions, {
      questions,
      questionsPerPage: 2,
    });

    expect(placed.step).toBe('page-2');
  });
});

describe('fieldId', () => {
  it('makes element ids from field names', () => {
    expect(fieldId(fields.file('resume'))).toBe('field-file-resume');
    expect(fieldId('firstName')).toBe('field-firstName');
  });
});
