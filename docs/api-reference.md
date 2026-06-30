# API Reference

All endpoints are served under `/api/` and require authentication unless noted otherwise. Responses use JSON. Enum values are serialized as strings.

Swagger UI is available at `/swagger` in Development mode. The OpenAPI spec is at `/openapi/v1.json`.

## Authentication

The app supports two auth schemes, selected per request: a **bearer JWT** when an
`Authorization: Bearer …` header is present, otherwise the **Identity cookie**.

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/auth/token` | POST | None | Exchange email + password for a signed JWT (for API clients) |
| `/account/login` | POST | None | Sign in with email and password (form POST, cookie-based) |
| `/account/logout` | POST | None | Sign out the current user |

### POST `/api/auth/token`

```json
{ "email": "admin@clinic.com", "password": "…" }
```

Returns a JWT plus its expiry and the user's roles. Send it on subsequent requests as
`Authorization: Bearer {token}`. The token carries the user's roles and their `clinic` (tenant)
claim, so every authenticated request is automatically scoped to that user's clinic.

```json
{ "token": "eyJ…", "expiresAt": "2026-07-01T12:00:00Z", "roles": ["Admin"] }
```

**Responses:** `200 OK` with the token, `401 Unauthorized` on invalid credentials. The endpoint
is rate-limited (per-IP `login` policy) to throttle credential stuffing.

Cookie login accepts `email`, `password`, and optional `returnUrl` as form fields. On success,
redirects to `returnUrl` or `/`. On failure, redirects to `/login?error=1` (bad credentials) or
`/login?error=2` (locked out).

## Appointments

**Authorization:** Staff or above (Admin, ClinicManager, Staff, Therapist)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/appointments` | GET | List all appointments |
| `/api/appointments/{id}` | GET | Get appointment by ID |
| `/api/appointments` | POST | Create a new appointment |
| `/api/appointments/{id}` | PUT | Update an appointment (reschedule, change status, notes) |
| `/api/appointments/{id}` | DELETE | Delete an appointment |
| `/api/appointments/{id}/mark-missed` | POST | Mark as missed and auto-reschedule |

### POST `/api/appointments`

```json
{
  "patientId": 1,
  "therapistId": 2,
  "roomId": 3,
  "startTime": "2025-03-15T09:00:00Z",
  "endTime": "2025-03-15T09:30:00Z",
  "treatmentPlanId": 1,
  "notes": "Initial evaluation"
}
```

**Responses:** `201 Created` with appointment DTO, `400 Bad Request` on validation error, `409 Conflict` on scheduling conflict.

### PUT `/api/appointments/{id}`

```json
{
  "startTime": "2025-03-16T10:00:00Z",
  "status": "Canceled",
  "treatmentPlanId": 1,
  "notes": "Rescheduled per patient request"
}
```

**Status values:** `Scheduled`, `Rescheduled`, `Completed`, `Canceled`, `Missed`

### POST `/api/appointments/{id}/mark-missed`

No request body. Returns the missed appointment and the newly created rescheduled appointment.

```json
{
  "missedAppointment": { ... },
  "rescheduledAppointment": { ... }
}
```

## Cancel Appointment Requests

**Authorization:** Authenticated users. Patients see only their own requests; staff see all.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/cancelappointmentrequests` | GET | List cancellation requests |
| `/api/cancelappointmentrequests/{id}` | GET | Get request by ID (Staff+) |
| `/api/cancelappointmentrequests` | POST | Submit a cancellation request |

### POST `/api/cancelappointmentrequests`

```json
{
  "appointmentId": 5,
  "reason": "Schedule conflict"
}
```

**Responses:** `201 Created`, `404 Not Found` (invalid appointment), `409 Conflict` (appointment not in Scheduled/Rescheduled status, or pending request already exists).

## Patients

**Authorization:** Staff or above for list/create/update/delete. Patients can view their own record.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/patients` | GET | List all patients |
| `/api/patients/{id}` | GET | Get patient by ID |
| `/api/patients` | POST | Create a patient |
| `/api/patients/{id}` | PUT | Update a patient |
| `/api/patients/{id}` | DELETE | Delete a patient |

### POST `/api/patients`

```json
{
  "firstName": "Jane",
  "lastName": "Doe",
  "email": "jane.doe@example.com",
  "dateOfBirth": "1990-05-15",
  "phone": "555-0100"
}
```

## Therapists

**Authorization:** Staff or above for read. Admin or ClinicManager for create/update/delete.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/therapists` | GET | List all therapists |
| `/api/therapists/{id}` | GET | Get therapist by ID |
| `/api/therapists` | POST | Create a therapist |
| `/api/therapists/{id}` | PUT | Update a therapist |
| `/api/therapists/{id}` | DELETE | Delete a therapist |

### POST `/api/therapists`

```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@clinic.com",
  "phone": "555-0200",
  "specialty": "Physical Therapy"
}
```

## Locations

**Authorization:** Authenticated for read. Admin or ClinicManager for create/update/delete.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/locations` | GET | List all locations |
| `/api/locations/{id}` | GET | Get location by ID |
| `/api/locations` | POST | Create a location |
| `/api/locations/{id}` | PUT | Update a location |
| `/api/locations/{id}` | DELETE | Delete a location |

### POST `/api/locations`

```json
{
  "name": "Downtown Clinic",
  "address": "123 Main St",
  "city": "Springfield",
  "state": "IL",
  "zipCode": "62701",
  "timeZone": "America/Chicago"
}
```

## Rooms

**Authorization:** Authenticated for read. Admin or ClinicManager for create/update/delete.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/rooms` | GET | List all rooms |
| `/api/rooms/{id}` | GET | Get room by ID |
| `/api/rooms/location/{locationId}` | GET | List rooms by location |
| `/api/rooms` | POST | Create a room |
| `/api/rooms/{id}` | PUT | Update a room |
| `/api/rooms/{id}` | DELETE | Delete a room |

### POST `/api/rooms`

```json
{
  "name": "Room A",
  "capacity": 2,
  "description": "Ground floor therapy room",
  "locationId": 1
}
```

## Therapy Types

**Authorization:** Authenticated for read. Admin or ClinicManager for create/update/delete.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/therapytypes` | GET | List all therapy types |
| `/api/therapytypes/{id}` | GET | Get therapy type by ID |
| `/api/therapytypes` | POST | Create a therapy type |
| `/api/therapytypes/{id}` | PUT | Update a therapy type |
| `/api/therapytypes/{id}` | DELETE | Delete a therapy type |

### POST `/api/therapytypes`

```json
{
  "name": "Occupational Therapy",
  "description": "Helps patients develop daily living skills",
  "specialty": "OT",
  "colorCode": "#4CAF50"
}
```

## Treatment Plans

**Authorization:** Staff or above

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/treatmentplans` | GET | List all treatment plans |
| `/api/treatmentplans/{id}` | GET | Get treatment plan by ID |
| `/api/treatmentplans` | POST | Create a treatment plan |
| `/api/treatmentplans/{id}` | PUT | Update a treatment plan |
| `/api/treatmentplans/{id}` | DELETE | Delete a treatment plan |

### POST `/api/treatmentplans`

```json
{
  "patientId": 1,
  "therapistId": 2,
  "frequencyPerWeek": 3,
  "totalDays": 30,
  "startDate": "2025-03-01",
  "therapyTypeIds": [1, 3]
}
```

## Waitlist

**Authorization:** Authenticated users. Patients act on their own entries; staff manage all.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/waitlist` | GET | List waitlist entries |
| `/api/waitlist/{id}` | GET | Get a waitlist entry by ID |
| `/api/waitlist` | POST | Add a patient to the waitlist for a date window |
| `/api/waitlist/{id}/cancel` | POST | Cancel a waitlist entry |
| `/api/waitlist/process` | POST | Attempt to fulfill waitlist entries against open slots (staff) |

When a matching opening exists, a waitlist entry is fulfilled by automatically booking the first
matching appointment and linking it back to the entry.

## Audit Logs

**Authorization:** Admin / Auditor.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/audit-logs` | GET | List audit-log entries (immutable change trail) |
| `/api/audit-logs/{id}` | GET | Get an audit-log entry by ID |

Audit entries are written automatically for every created/modified/deleted entity; sensitive
fields are redacted in the change summary.

## Pagination

List endpoints accept optional `page` and `pageSize` query parameters (default page size 50, max
200). When neither is supplied the endpoint returns the full unpaged list (legacy behavior). When
paging is requested, the total (unpaged) row count is returned in the `X-Total-Count` response
header.

## Common Response Patterns

All endpoints follow standard HTTP status codes:

| Status | Meaning |
|--------|---------|
| `200 OK` | Successful read or update |
| `201 Created` | Resource created (includes `Location` header) |
| `204 No Content` | Successful update or delete |
| `400 Bad Request` | Validation error (message in body) |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Insufficient role |
| `404 Not Found` | Resource does not exist |
| `409 Conflict` | Business rule violation (details in `ProblemDetails`) |

Error responses for `409 Conflict` use the `ProblemDetails` format:

```json
{
  "detail": "Cannot request cancellation for an appointment with status Completed."
}
```
