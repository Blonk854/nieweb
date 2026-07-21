import { describe, it, expect, beforeEach } from "vitest";
import { useSessionStore } from "./session";

describe("useSessionStore", () => {
    beforeEach(() => {
        useSessionStore.getState().clear();
        localStorage.clear();
    });

    it("starts empty", () => {
        expect(useSessionStore.getState().user).toBeNull();
        expect(useSessionStore.getState().token).toBeNull();
    });

    it("setSession stores the user + token", () => {
        useSessionStore.getState().setSession(
            {
                email: "line@nieweb.test",
                displayName: "Line Eng",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            "eyJ.test.token",
        );
        expect(useSessionStore.getState().user?.email).toBe("line@nieweb.test");
        expect(useSessionStore.getState().token).toBe("eyJ.test.token");
    });

    it("clear wipes user + token", () => {
        useSessionStore.getState().setSession(
            { email: "x@y.z", displayName: "X", roles: [], mustRotatePassword: false },
            "tok",
        );
        useSessionStore.getState().clear();
        expect(useSessionStore.getState().user).toBeNull();
        expect(useSessionStore.getState().token).toBeNull();
    });

    it("persists to localStorage under nieweb.session.v1", () => {
        useSessionStore.getState().setSession(
            {
                email: "p@q.r",
                displayName: "P",
                roles: ["Reader"],
                mustRotatePassword: false,
            },
            "pTok",
        );
        const raw = localStorage.getItem("nieweb.session.v1");
        expect(raw).not.toBeNull();
        const parsed = JSON.parse(raw!);
        expect(parsed.state.token).toBe("pTok");
        expect(parsed.state.user.email).toBe("p@q.r");
    });
});
