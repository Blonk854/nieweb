import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MantineProvider, createTheme } from "@mantine/core";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { RouterProvider } from "@tanstack/react-router";

import { router } from "./router/router";
import { createQueryClient } from "./query/queryClient";
import "@mantine/core/styles.css";
import "./index.css";

const theme = createTheme({
    primaryColor: "blue",
    defaultRadius: "md",
    fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
});

const queryClient = createQueryClient();

const container = document.getElementById("root");
if (!container) {
    throw new Error("Root container #root is missing from index.html");
}

createRoot(container).render(
    <StrictMode>
        <MantineProvider theme={theme} defaultColorScheme="auto">
            <QueryClientProvider client={queryClient}>
                <RouterProvider router={router} />
                {import.meta.env.DEV && (
                    <ReactQueryDevtools initialIsOpen={false} />
                )}
            </QueryClientProvider>
        </MantineProvider>
    </StrictMode>,
);
