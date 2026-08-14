// Test stub for @/api/endpoints.
//
// Every endpoint offlineQueue.replayAction can reach delegates to a single
// programmable behaviour so a test can make the *next* replay throw a chosen
// error. The error thrown is the REAL ApiError from src/api/client.ts — the
// point of the harness is to exercise the real class, not a look-alike.

let behaviour = async () => undefined;
let calls = 0;

/** Install the behaviour used by every stubbed endpoint. */
export function __setBehaviour(fn) { behaviour = fn; calls = 0; }
/** How many times any endpoint was invoked since __setBehaviour. */
export function __callCount() { return calls; }

const NAMES = [
  'createIssue', 'updateIssue', 'transitionCDE', 'uploadIssueAttachment',
  'captureSitePhoto', 'addIssueComment', 'addMeetingAction',
  'updateMeetingAction', 'createSiteDiary', 'updateSiteDiary',
  'signOffStageCriterion', 'uploadAudioNote', 'uploadModelMarkup',
  'fulfilChecklistItem', 'createDeliverable', 'updateDeliverable',
  'transitionDeliverable', 'postMgasVerification', 'postPressureLog',
  'postAntiLigatureAudit',
];

const impl = {};
for (const n of NAMES) {
  impl[n] = async (...args) => { calls++; return behaviour(n, args); };
}

export const createIssue = impl.createIssue;
export const updateIssue = impl.updateIssue;
export const transitionCDE = impl.transitionCDE;
export const uploadIssueAttachment = impl.uploadIssueAttachment;
export const captureSitePhoto = impl.captureSitePhoto;
export const addIssueComment = impl.addIssueComment;
export const addMeetingAction = impl.addMeetingAction;
export const updateMeetingAction = impl.updateMeetingAction;
export const createSiteDiary = impl.createSiteDiary;
export const updateSiteDiary = impl.updateSiteDiary;
export const signOffStageCriterion = impl.signOffStageCriterion;
export const uploadAudioNote = impl.uploadAudioNote;
export const uploadModelMarkup = impl.uploadModelMarkup;
export const fulfilChecklistItem = impl.fulfilChecklistItem;
export const createDeliverable = impl.createDeliverable;
export const updateDeliverable = impl.updateDeliverable;
export const transitionDeliverable = impl.transitionDeliverable;
export const postMgasVerification = impl.postMgasVerification;
export const postPressureLog = impl.postPressureLog;
export const postAntiLigatureAudit = impl.postAntiLigatureAudit;
