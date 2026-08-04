# API Reference

<!-- Generated from the API's OpenAPI document by `just docs::api`. Do not edit: your changes will be overwritten, and Trackr.Api.Tests fails when this page and the code disagree. -->

Every route Trackr serves, generated from the API itself. Routes are relative to your server's address — `frontend` proxies everything under `/api/` to the backend.

Two authentication schemes reach these endpoints: the website sends an HttpOnly session cookie, and the Android app sends a bearer token. See [Accounts and 2FA](Accounts-and-2FA).

> **Response bodies are not listed.** The handlers return `IResult` and declare no response type, so the OpenAPI document does not know their shapes and neither does this page. The request bodies and schemas below are complete. For what a response actually contains, read the DTOs in `Trackr.Shared` — they are the same types the clients deserialise into.

## Account

### `GET /api/account/2fa`

Whether 2FA is on, and how many recovery codes remain.

**Responses:** `200`

### `POST /api/account/2fa/disable`

Turn 2FA off. Requires the account password.

**Request body:** `application/json` → [`DisableTwoFactorRequest`](#disabletwofactorrequest)

**Responses:** `200`

### `POST /api/account/2fa/enable`

Finish enrolment by proving the authenticator works. Returns recovery codes once.

**Request body:** `application/json` → [`TwoFactorCodeRequest`](#twofactorcoderequest)

**Responses:** `200`

### `POST /api/account/2fa/enroll`

Start enrolment: returns the shared secret and a QR code to scan.

**Responses:** `200`

### `POST /api/account/2fa/recovery-codes`

Replace the recovery codes. Returns the new set once.

**Responses:** `200`

### `GET /api/account/avatar`

The profile picture. Supports If-None-Match; 404 when there is none.

**Responses:** `200`

### `PUT /api/account/avatar`

Replace the profile picture. Body is the raw image bytes.

**Responses:** `200`

### `DELETE /api/account/avatar`

Remove the profile picture, falling back to initials.

**Responses:** `200`

### `POST /api/account/password`

Change the password, re-checking the current one first.

**Request body:** `application/json` → [`ChangePasswordRequest`](#changepasswordrequest)

**Responses:** `200`

## Auth

### `POST /api/auth/forgot-password`

Send a password reset link. Always reports success.

**Request body:** `application/json` → [`ForgotPasswordRequest`](#forgotpasswordrequest)

**Responses:** `200`

### `POST /api/auth/login`

Password sign-in. May report that a 2FA code is still owed.

**Request body:** `application/json` → [`LoginRequest`](#loginrequest)

**Responses:** `200`

### `POST /api/auth/login/2fa`

Second step of sign-in: a code from the authenticator app.

**Request body:** `application/json` → [`TwoFactorLoginRequest`](#twofactorloginrequest)

**Responses:** `200`

### `POST /api/auth/login/recovery-code`

Second step of sign-in using a single-use recovery code instead.

**Request body:** `application/json` → [`RecoveryCodeLoginRequest`](#recoverycodeloginrequest)

**Responses:** `200`

### `POST /api/auth/logout`

Clear the session cookie.

**Responses:** `200`

### `GET /api/auth/me`

Who the current session belongs to. The client's source of auth state.

**Responses:** `200`

### `POST /api/auth/register`

Create an account: the first one on an empty database, or one with an invite.

**Request body:** `application/json` → [`RegisterRequest`](#registerrequest)

**Responses:** `200`

### `GET /api/auth/registration-status`

Whether the next account may be created freely or needs an invite.

**Responses:** `200`

### `POST /api/auth/reset-password`

Set a new password using a token from the reset link.

**Request body:** `application/json` → [`ResetPasswordRequest`](#resetpasswordrequest)

**Responses:** `200`

### `POST /api/auth/token`

Password sign-in for a native client, returning bearer tokens.

**Request body:** `application/json` → [`TokenRequest`](#tokenrequest)

**Responses:** `200`

### `POST /api/auth/token/refresh`

Exchange a refresh token for a new access token.

**Request body:** `application/json` → [`RefreshRequest`](#refreshrequest)

**Responses:** `200`

## Foods

### `GET /api/foods`

Catalog items visible to the caller: their own, plus everything shared.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `search` | query | no | string |
| `visibility` | query | no | [`FoodVisibility`](#foodvisibility) |

**Responses:** `200`

### `POST /api/foods`

Add an item to the catalog. Personal unless the request says otherwise.

**Request body:** `application/json` → [`SaveFoodItemRequest`](#savefooditemrequest)

**Responses:** `200`

### `GET /api/foods/{id}`

One catalog item, with its full nutrient map.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

### `PUT /api/foods/{id}`

Replace an item, including its whole nutrient map.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Request body:** `application/json` → [`SaveFoodItemRequest`](#savefooditemrequest)

**Responses:** `200`

### `DELETE /api/foods/{id}`

Delete a personal item. Already-logged entries keep their snapshots.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

### `POST /api/foods/{id}/share`

Promote a personal item to the shared catalog. One-way.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

## Health

### `GET /api/health`

Full health report, including database connectivity.

**Responses:** `200`

### `GET /api/health/live`

Liveness probe. Always 200 while the process is running.

**Responses:** `200`

## Images

### `POST /api/images`

Upload a meal photo. Body is the raw image bytes; it starts unattached.

**Responses:** `200`

### `GET /api/images/{id}`

The photo's bytes. Supports If-None-Match.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

### `DELETE /api/images/{id}`

Remove a photo.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

## Invites

### `POST /api/invites`

Mint a single-use registration invite. The token is returned once.

**Request body:** `application/json` → [`CreateInviteRequest`](#createinviterequest)

**Responses:** `200`

### `GET /api/invites`

All invites and their current state.

**Responses:** `200`

### `DELETE /api/invites/{id}`

Revoke an unused invite.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

## Log

### `GET /api/log`

Log entries for a range of local days. Defaults to today.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `from` | query | no | string (date) |
| `to` | query | no | string (date) |

**Responses:** `200`

### `POST /api/log`

Record a meal: the entry, its items and any photos, in one request.

**Request body:** `application/json` → [`SaveLogEntryRequest`](#savelogentryrequest)

**Responses:** `200`

### `GET /api/log/{id}`

One log entry, with its items and photo metadata.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

### `PUT /api/log/{id}`

Replace an entry, its items and its photo set.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Request body:** `application/json` → [`SaveLogEntryRequest`](#savelogentryrequest)

**Responses:** `200`

### `DELETE /api/log/{id}`

Delete an entry, its items and its photos.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

## Lookup

### `GET /api/lookup/barcode/{barcode}`

Ask Open Food Facts about a barcode. Writes nothing.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `barcode` | path | yes | string |

**Responses:** `200`

### `POST /api/lookup/image/{id}`

Read a barcode out of an uploaded meal photo and look it up. Writes nothing.

| Parameter | In | Required | Type |
| --- | --- | --- | --- |
| `id` | path | yes | string (uuid) |

**Responses:** `200`

## Nutrients

### `GET /api/nutrients`

Every nutrient the server can record, in nutrition-label order.

**Responses:** `200`

## Schemas

The request and response shapes above. These are the DTOs in `Trackr.Shared`, which the web app and the Android app reference directly rather than generating a client from this document.

### `ChangePasswordRequest`

| Property | Type | Required |
| --- | --- | --- |
| `currentPassword` | string | yes |
| `newPassword` | string | yes |

### `CreateInviteRequest`

| Property | Type | Required |
| --- | --- | --- |
| `note` | string, nullable | no |
| `expiresInHours` | integer or string (int32) | no |

### `DisableTwoFactorRequest`

| Property | Type | Required |
| --- | --- | --- |
| `password` | string | yes |

### `FoodSource`

No properties.

### `FoodVisibility`

No properties.

### `ForgotPasswordRequest`

| Property | Type | Required |
| --- | --- | --- |
| `email` | string | yes |

### `LoginRequest`

| Property | Type | Required |
| --- | --- | --- |
| `email` | string | yes |
| `password` | string | yes |
| `rememberMe` | boolean | no |

### `RecoveryCodeLoginRequest`

| Property | Type | Required |
| --- | --- | --- |
| `recoveryCode` | string | yes |

### `RefreshRequest`

| Property | Type | Required |
| --- | --- | --- |
| `refreshToken` | string | yes |

### `RegisterRequest`

| Property | Type | Required |
| --- | --- | --- |
| `email` | string | yes |
| `password` | string | yes |
| `inviteToken` | string, nullable | no |

### `ResetPasswordRequest`

| Property | Type | Required |
| --- | --- | --- |
| `email` | string | yes |
| `code` | string | yes |
| `newPassword` | string | yes |

### `SaveFoodComponentRequest`

| Property | Type | Required |
| --- | --- | --- |
| `foodItemId` | string (uuid) | no |
| `quantity` | number or string (double) | no |

### `SaveFoodItemRequest`

| Property | Type | Required |
| --- | --- | --- |
| `name` | string | yes |
| `brand` | string, nullable | no |
| `barcode` | string, nullable | no |
| `servingSize` | number or string (double) | no |
| `servingUnit` | string | yes |
| `source` | [`FoodSource`](#foodsource) | no |
| `visibility` | [`FoodVisibility`](#foodvisibility) | no |
| `energyKcal` | number or string (double) | no |
| `fatG` | number or string (double) | no |
| `carbohydrateG` | number or string (double) | no |
| `proteinG` | number or string (double) | no |
| `nutrients` | object | no |
| `yield` | number or string (double), nullable | no |
| `components` | array of [`SaveFoodComponentRequest`](#savefoodcomponentrequest) | no |

### `SaveLogEntryRequest`

| Property | Type | Required |
| --- | --- | --- |
| `loggedUtc` | string (date-time), nullable | no |
| `note` | string, nullable | no |
| `items` | array of [`SaveLogItemRequest`](#savelogitemrequest) | no |
| `imageIds` | array of string (uuid) | no |

### `SaveLogItemRequest`

| Property | Type | Required |
| --- | --- | --- |
| `foodItemId` | string (uuid), nullable | no |
| `name` | string | yes |
| `brand` | string, nullable | no |
| `quantity` | number or string (double) | no |
| `servingSize` | number or string (double), nullable | no |
| `servingUnit` | string, nullable | no |
| `energyKcal` | number or string (double) | no |
| `fatG` | number or string (double) | no |
| `carbohydrateG` | number or string (double) | no |
| `proteinG` | number or string (double) | no |
| `nutrients` | object | no |

### `TokenRequest`

| Property | Type | Required |
| --- | --- | --- |
| `email` | string | yes |
| `password` | string | yes |
| `twoFactorCode` | string, nullable | no |
| `twoFactorRecoveryCode` | string, nullable | no |

### `TwoFactorCodeRequest`

| Property | Type | Required |
| --- | --- | --- |
| `code` | string | yes |

### `TwoFactorLoginRequest`

| Property | Type | Required |
| --- | --- | --- |
| `code` | string | yes |
| `rememberMe` | boolean | no |
| `rememberMachine` | boolean | no |

