# Progress — explorer_survey_1

- **Status**: Investigation complete, authoring survey report and handoff
- **Last visited**: 2026-10-27T19:19:00Z
- **Current task**: Writing survey_security.md and handoff.md
- **Findings summary**:
  1. Complete lifecycle mapped (Customer -> Admin Review -> Admin SetPrice -> Customer Respond -> Admin Assign -> Driver EndTrip -> Admin PreparePayments). InProgress status is unhandled by driver/controller actions.
  2. IDOR identified in CustomerController.OrderSuccess (returns 302 redirect instead of 404/403). Gaps noted for future tracking and invoice download actions.
  3. No concurrency tokens (RowVersion / ConcurrencyCheck) exist on Trip, DriverTrip, or Driver. Race conditions in ReviewTrip, SetPrice, RespondToPrice, AssignTrip, EndTrip.
  4. All existing mutating actions have [ValidateAntiForgeryToken], but missing global AutoValidateAntiforgeryTokenAttribute.
  5. User-supplied strings (FromAddress, ToAddress, FromCity, ToCity, Notes) lack length capping on Notes and lack sanitization, which are interpolated directly into SendGrid email messages.
