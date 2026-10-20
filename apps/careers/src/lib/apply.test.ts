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
    const state = { ...emptyState(), fullName: 'Casey Morgan', answers: { shifts: ['mornings'] }, files: { resume: upload } };

    expect(parseState(JSON.stringify(state))).toEqual(state);
  });

  it('falls back to empty for anything malformed, and drops bad uploads', () => {
    expect(parseState('{not json')).toEqual(emptyState());
    expect(parseState(null)).toEqual(emptyState());
    const tampered = parseState(JSON.stringify({ fullName: 42, aiUseDeclaration: 'Maybe', files: { resume: { uploadId: '../../x' } } }));
    expect(tampered).toEqual(emptyState());
  });
});

describe('reading a step', () => {
  it('reads details, trims them and reads the assistive technology switch', () => {
    const state = readDetails(form([['fullName', '  Casey Morgan '], ['email', 'casey@example.com'], ['accommodationRequested', 'on']]), emptyState());

    expect(state).toMatchObject({ fullName: 'Casey Morgan', email: 'casey@example.com', phone: '', accommodationRequested: true });
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
  it('asks for a name, a real-looking email and the required resume', () => {
    expect(validateDetails({ ...emptyState(), email: 'not-an-email' }, questions)).toEqual({
      fullName: 'Enter your full name.',
      email: 'Enter an email address like name@example.com.',
      [fields.file('resume')]: 'Add a file.',
    });
    expect(validateDetails({ ...emptyState(), fullName: 'Casey', email: 'casey@example.com', files: { resume: upload } }, questions)).toEqual({});
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
      fullName: 'Casey Morgan',
      email: 'casey@example.com',
      accommodationRequested: true,
      answers: { work_authorization: 'yes', shifts: ['mornings'] },
      files: { resume: upload },
      aiUseDeclaration: 'Used' as const,
      aiUseDetails: 'Grammar',
    };

    expect(toRequest(state, questions, { honeypotFilled: true, botCheckToken: null })).toEqual({
      candidate: { fullName: 'Casey Morgan', email: 'casey@example.com', phone: null },
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
});

describe('fieldId', () => {
  it('makes element ids from field names', () => {
    expect(fieldId(fields.file('resume'))).toBe('field-file-resume');
    expect(fieldId('fullName')).toBe('field-fullName');
  });
});
