/**
 * XMLHttpRequest transport for Sentry.
 *
 * Gameface exposes no Fetch API at all — a runtime probe on 0.3.30 logged
 * `fetch=undefined Headers=undefined Request=undefined Response=undefined` — while the Sentry
 * browser SDK has shipped a fetch-only transport since v8. Every event the SDK collected was
 * therefore dropped at the send step: the dashboard held zero events for the reporter's whole
 * lifetime, even though the same JS errors reached prod through the C# log channel.
 *
 * XMLHttpRequest is available — the vanilla CS2 UI does all of its networking through it. This
 * restores the transport the SDK dropped, wired through createTransport so buffering, rate-limit
 * handling and dropped-event bookkeeping remain the SDK's. Headers are only set when the SDK asks
 * for them: with none, the POST stays a simple request and needs no CORS preflight, which matters
 * on a page served from a coui:// origin.
 */

import { createTransport } from "@sentry/core";
import type {
    BaseTransportOptions,
    Transport,
    TransportMakeRequestResponse,
    TransportRequest,
} from "@sentry/core";

import { scLog } from "../utils/logging";

// One line per session on the first response, so the mod log alone answers "does the channel
// deliver" — status 200 is a live channel, 0 means the request never left, 4xx is a server refusal.
let firstResponseLogged = false;

/**
 * Build a Sentry transport that sends envelopes over XMLHttpRequest.
 *
 * @param options Transport options supplied by the SDK (ingest url, optional headers).
 */
export function makeXhrTransport(options: BaseTransportOptions): Transport {
    function makeRequest(request: TransportRequest): PromiseLike<TransportMakeRequestResponse> {
        return new Promise<TransportMakeRequestResponse>((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            xhr.onerror = (): void => {
                if (!firstResponseLogged) {
                    firstResponseLogged = true;
                    scLog("[CrashReporter] xhr transport: request failed before a response arrived");
                }
                reject(new Error("Sentry XHR transport: request failed"));
            };

            xhr.onreadystatechange = (): void => {
                if (xhr.readyState !== XMLHttpRequest.DONE) return;

                if (!firstResponseLogged) {
                    firstResponseLogged = true;
                    scLog(`[CrashReporter] xhr transport first response: status=${xhr.status}`);
                }

                resolve({
                    statusCode: xhr.status,
                    headers: {
                        "x-sentry-rate-limits": xhr.getResponseHeader("X-Sentry-Rate-Limits"),
                        "retry-after": xhr.getResponseHeader("Retry-After"),
                    },
                });
            };

            xhr.open("POST", options.url);

            for (const [header, value] of Object.entries(options.headers ?? {})) {
                xhr.setRequestHeader(header, value);
            }

            // TS 5.7 made ArrayBufferView generic over ArrayBufferLike, so the SDK's
            // `string | Uint8Array<ArrayBufferLike>` no longer matches XMLHttpRequestBodyInit in
            // the bundled DOM lib. Both variants are valid XHR bodies at runtime; the assertion
            // bridges the lib gap only, it does not widen what can actually arrive here.
            xhr.send(request.body as XMLHttpRequestBodyInit);
        });
    }

    return createTransport(options, makeRequest);
}
