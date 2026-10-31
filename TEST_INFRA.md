# E2E Test Infra: Logi-Track Overhaul

## Test Philosophy
- Requirement-driven, opaque-box testing directly validating the Acceptance Criteria of ORIGINAL_REQUEST.md.
- Methodology: Category-Partition, Boundary Value Analysis, Pairwise Interaction Testing, and Realistic Application Scenarios.

## Feature Inventory & Test Mapping
| # | Feature | Source (Requirement) | Tier 1 (Functional) | Tier 2 (Boundary/Corner) | Tier 3 (Cross-Feature) | Tier 4 (Scenario) |
|---|---------|----------------------|:-------------------:|:------------------------:|:----------------------:|:-----------------:|
| 1 | IDOR Trip Retrieval Guard | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 2 | Optimistic Concurrency on Updates | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 3 | Transaction Guards on Assignment | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 4 | Trip State Machine Harmonization | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 5 | Input Sanitization & Global CSRF | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 6 | Interactive Map Rendering | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 7 | City Coordinate Resolution | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 8 | SignalR Hub Location Broadcasting | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 9 | SignalR Driver/Admin Authorization | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 10 | Group Location Broadcast Isolation | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 11 | PDF Invoicing Generation | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 12 | Customer & Admin PDF Downloads | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 13 | Completed Status Validation for PDF | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |

## Test Architecture
- Test Project: `tests/LogiTrack.Tests/LogiTrack.Tests.csproj`
- Runner: xUnit with Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory<Program>`) and isolated SQLite temporary databases per fixture.
- SignalR Testing: In-memory hub test harness with `HubConnection` or mocked `IHubCallerClients` validating group calls and claim checks.
- PDF Testing: Byte array stream validation verifying `%PDF-` magic header, non-zero length, document metadata (Title, Author, Subject), and text elements (TripId, Fare Breakdown, Status).

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Complete Trip Lifecycle with Security Checks | F1, F4, F5, F11, F12 | High |
| 2 | Concurrent Admin Price Conflict Resolution | F2, F3 | High |
| 3 | Live Driver Simulation & Multi-Tenant Isolation | F6, F8, F9, F10 | High |
| 4 | Unauthorized Access & Malicious Input Tampering | F1, F5, F12, F13 | High |
| 5 | End-to-End Delivery to Invoice Generation | F4, F8, F11, F12, F13 | High |

## Coverage Thresholds
- Tier 1: Functional coverage across all security, tracking, and invoicing endpoints.
- Tier 2: Boundary value testing (invalid IDs, boundary coordinates, stale concurrency tokens, uncompleted trips).
- Tier 3: Cross-feature interactions (concurrent driver assignment during status changes, SignalR disconnects during state transitions).
- Tier 4: End-to-end full user workflows.
- Acceptance criteria: 0 build warnings, 0 build errors, 100% tests passing.
