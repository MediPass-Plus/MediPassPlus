# Visit-access QR tool (development helper)

MP-97 adds a small browser page that exercises the whole visit-access QR loop by hand:
the doctor's `start-visit` response, the QR image, camera scanning, the patient's decision, and the
derived access state. It exists so the feature can be demonstrated through Swagger without a frontend
screen; it is not a product UI and the Angular app is unchanged.

The page is `src/MediPassPlus.Api/wwwroot/visit-access-tool.html`. The API serves it, together with the
injected Swagger topbar link (`wwwroot/swagger-visit-access-tool.js`), only when the Swagger document is
exposed: Development, or `OpenApi:ExposeOutsideDevelopment=true`. Outside Development the default is
`false`, so the helper is not reachable in a deployment that holds real data.

## What you need

- The API running on `http://localhost:5114` (`dotnet run --project src/MediPassPlus.Api --launch-profile http`).
- A checked-in appointment for today, owned by a doctor account, and the patient's account.
- A browser with a camera. `getUserMedia` needs a secure context: `http://localhost` works, a LAN address
  over plain HTTP does not (use HTTPS or the browser's localhost exception).
- A patient JWT from `POST api/Auth/login` (copy `accessToken`, without the `Bearer ` prefix).

## Steps

1. **Doctor: start the visit.** In Swagger, call
   `POST api/appointments/{appointmentId}/start-visit` as the doctor. The `200` response contains:
   - `qrToken` — the plaintext Base64Url token, valid for two minutes;
   - `qrCodeDataUri` — the same token as a `data:image/png;base64,...` image;
   - `accessRequestId` and `expiresAtUtc`.
2. **Open the tool.** Click **Visit access QR tool** in the Swagger topbar, or open
   `http://localhost:5114/visit-access-tool.html`.
3. **Paste the patient JWT** into section 1. The page keeps it in memory only; it is never stored or logged.
4. **Display the QR.** Paste `qrCodeDataUri` into section 2 and click **Display QR**. This is the code the
   patient scans. You can also paste `qrToken` into section 3 to skip the camera.
5. **Scan as the patient.** Start the camera, point it at the QR, and click **Capture & scan**. The page
   posts the frame to `POST api/access-requests/scan-image`; the server decodes it in memory and returns
   the request, doctor, specialty, clinic, address and appointment start. A text token uses
   `POST api/access/scan`.
6. **Decide.** **Approve access** creates the two-hour grant; **Reject** creates none and revokes the
   credentials. A text scan calls `POST api/access-requests/token/approve` or `/token/reject`; a camera scan
   calls the request-id routes, because the browser never receives the decoded token.
7. **Read the state.** `GET api/appointments/{appointmentId}/access-state` returns the request status, grant
   status, two-hour limit, scheduled/actual end and `remainingSeconds` to the appointment's doctor or patient.

## Behaviour and limits

- A scan is read-only. It never consumes the token; only approve or reject does.
- The token lives two minutes. If it expires, the doctor runs `start-visit` again while the visit is still
  `InVisit`; a second start is refused once a grant exists.
- Unknown and other-patient tokens answer `403`. Expired, consumed, revoked or answered tokens and closed
  visits answer `410` on the token routes; the request-id approve route answers `409` for a closed visit.
- Scan, scan-image and both token decisions share the per-patient `QrApprove` fixed window (default
  5 attempts / 15 minutes, configurable). Beyond it the API answers `429` with `Retry-After`. Use a fresh
  patient for a repeated demo.
- Uploads must be `image/*` and at most 5 MB; anything else is `415` or `400`.
- The page never logs or stores the JWT, the token or the camera frame. The camera stream stops when you
  click **Stop** or leave the page.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Camera does not start | The page is not on `localhost`/HTTPS, or the browser denied camera permission. |
| `401` on every call | The JWT is missing, expired, or was pasted with a `Bearer ` prefix. |
| `403` on scan | The token is unknown, belongs to another patient, or the JWT is not a patient account. |
| `410` on scan/approve | The two-minute token expired, was already used, or the visit is no longer in progress. |
| `429` with `Retry-After` | The patient exhausted the `QrApprove` window; wait or use another patient. |
| `415` on scan-image | The uploaded part's content type is not `image/*`. |
