import { Alert } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { useTranslation } from "react-i18next";

import { describeApiError } from "../api/problem";

export type ApiErrorAlertProps = {
    /** The thrown value from a query / mutation. Nothing renders when nullish. */
    error: unknown;
    /** Forwarded to the Mantine `Alert` for targeted assertions. */
    "data-testid"?: string;
};

/**
 * The single place the app renders a failed API call. Every report screen
 * uses it so a 400 reads as an actionable "check your filters" message
 * instead of the old blanket "Could not reach the API — HTTP 400".
 */
export function ApiErrorAlert({ error, "data-testid": testId }: ApiErrorAlertProps) {
    const { t } = useTranslation();
    if (error === null || error === undefined) {
        return null;
    }
    const { title, message } = describeApiError(error, t);
    return (
        <Alert
            color="red"
            icon={<IconAlertTriangle size={18} />}
            title={title}
            role="alert"
            data-testid={testId}
        >
            {message}
        </Alert>
    );
}
