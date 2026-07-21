import { useEffect, useMemo, useState } from "react";
import { Alert, Card, Loader, Stack, Text, Title } from "@mantine/core";
import { useNavigate } from "@tanstack/react-router";
import { IconAlertCircle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { whoami } from "../api/auth";
import { useSessionStore } from "../state/session";

/**
 * Post-OIDC landing route. The API's `/auth/oidc/callback-return`
 * endpoint issues a JWT and redirects the browser here with the
 * following URL fragment payload:
 *
 *   #accessToken=...&expiresUtc=<ISO>&mustRotatePassword=true|false&returnUrl=<local>
 *
 * (or, on failure)
 *
 *   #error=<Outcome>&message=<url-encoded>
 *
 * The fragment stays entirely client-side (never sent to any server),
 * which keeps the JWT out of proxy access logs. We parse it, hydrate
 * the session store (mirroring the local sign-in mutation in
 * `login.tsx`), immediately clear the fragment from the address bar
 * so the token can't be recovered by a Back navigation, then bounce
 * the user to `returnUrl` (or the SPA root on failure).
 */
export function OidcReturnRoute() {
    const { t } = useTranslation();
    const navigate = useNavigate();
    const setSession = useSessionStore((s) => s.setSession);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    // Parse the fragment once on mount. `useMemo` avoids re-parsing on
    // every re-render when the effect below updates state.
    const params = useMemo(() => {
        if (typeof window === "undefined") {
            return new URLSearchParams();
        }
        const raw = window.location.hash.replace(/^#/, "");
        return new URLSearchParams(raw);
    }, []);

    useEffect(() => {
        const errorCode = params.get("error");
        if (errorCode) {
            const raw = params.get("message") ?? "";
            let localised: string;
            if (errorCode === "LocalAccountConflict") {
                localised = t("login.form.ssoErrorLocalConflict");
            } else if (errorCode === "MissingRequiredClaim") {
                localised = t("login.form.ssoError");
            } else {
                localised = t("login.form.ssoUnknownError", { message: raw || errorCode });
            }
            setErrorMessage(localised);
            // Scrub the fragment before we redirect so a Back click
            // doesn't leak the error / token pair.
            if (typeof window !== "undefined") {
                window.history.replaceState(null, "", window.location.pathname);
            }
            return;
        }

        const accessToken = params.get("accessToken");
        const returnUrlRaw = params.get("returnUrl") || "/app/";
        // Server hands us a full site path (e.g. `/app/report/panel-yield`);
        // the router works in its own basepath-relative space so we
        // strip the `/app` prefix. Anything unexpected falls back to
        // the SPA root.
        const returnUrl =
            returnUrlRaw.startsWith("/app/")
                ? returnUrlRaw.slice("/app".length) || "/"
                : returnUrlRaw === "/app"
                ? "/"
                : "/";
        const mustRotate = params.get("mustRotatePassword") === "true";

        if (!accessToken) {
            setErrorMessage(t("login.form.ssoError"));
            return;
        }

        let cancelled = false;
        // Optimistically seed the store so `apiFetch` inside `whoami`
        // sees the fresh bearer.
        setSession(
            {
                email: "",
                displayName: "",
                roles: [],
                mustRotatePassword: mustRotate,
            },
            accessToken,
        );
        // Immediately scrub the fragment; the token is in the store now.
        if (typeof window !== "undefined") {
            window.history.replaceState(null, "", window.location.pathname);
        }

        void (async () => {
            try {
                const me = await whoami();
                if (cancelled) {
                    return;
                }
                setSession(
                    {
                        email: me.email ?? "",
                        displayName: me.name ?? me.email ?? "",
                        roles: me.roles,
                        mustRotatePassword: me.mustRotatePassword,
                    },
                    accessToken,
                );
                // Force-rotation accounts get bounced to the change-
                // password screen; the router guard will keep them
                // there until the flag clears.
                if (me.mustRotatePassword) {
                    void navigate({ to: "/account/password" });
                    return;
                }
                // Normalise returnUrl to a local path; if navigation is
                // rejected TanStack Router falls back to the SPA root.
                void navigate({ to: returnUrl });
            } catch (err) {
                if (cancelled) {
                    return;
                }
                setErrorMessage(
                    t("login.form.ssoUnknownError", {
                        message: err instanceof Error ? err.message : String(err),
                    }),
                );
            }
        })();

        return () => {
            cancelled = true;
        };
    }, [params, setSession, navigate, t]);

    if (errorMessage) {
        return (
            <Stack gap="lg" maw={480}>
                <Title order={2}>{t("login.title")}</Title>
                <Card withBorder padding="lg" radius="md">
                    <Alert
                        color="red"
                        icon={<IconAlertCircle size={18} />}
                        role="alert"
                    >
                        {errorMessage}
                    </Alert>
                </Card>
            </Stack>
        );
    }

    return (
        <Stack gap="md" align="center" py="xl">
            <Loader />
            <Text>{t("login.form.signingIn")}</Text>
        </Stack>
    );
}
