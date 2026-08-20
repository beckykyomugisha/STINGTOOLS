namespace Planscape.Core.Entities;

/// <summary>
/// One CDE state a document may move to next, and whether that move needs an
/// approval record first.
///
/// WHY THIS IS SERVED RATHER THAN RE-DERIVED
/// -----------------------------------------
/// The ISO 19650-2 state machine lives in <c>DocumentsController</c> as two
/// dictionaries — <c>ValidTransitions</c> and <c>ApprovalRequiredTransitions</c>
/// — and the server enforces both on every transition. Mobile kept its own copy
/// of each, and both had drifted, in opposite directions:
///
///   VALID: mobile said PUBLISHED -> [ARCHIVE]. The server also allows
///          SUPERSEDED and WITHDRAWN, and SHARED -> WITHDRAWN. Three legal
///          transitions the user could not see.
///
///   APPROVAL: mobile said {WIP->SHARED, SHARED->PUBLISHED}. The server says
///          {SHARED->PUBLISHED, PUBLISHED->SUPERSEDED}. So WIP->SHARED was
///          routed through the approval workflow the server does not require,
///          and PUBLISHED->SUPERSEDED was sent straight at the transition
///          endpoint, which refuses it for want of an approval record.
///
/// Every one of those is a client guessing at a rule the server already knows.
/// This type is the answer, sent with the document.
///
/// AFFORDANCE, NOT AUTHORITY
/// -------------------------
/// Computed from the two state-machine dictionaries ONLY. It deliberately does
/// not fold in the per-folder ACL or <c>TransitionRoleRequirements</c>: those
/// are per-caller and per-document-slice, and answering them here would turn a
/// cheap projection into a per-row authorization pass while implying the list
/// is a permission grant. The server still gates every transition. This says
/// what the STATE MACHINE permits, so a client stops inventing that part.
/// </summary>
public sealed record CdeTransitionOption(string To, bool RequiresApproval);
