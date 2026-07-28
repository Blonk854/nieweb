import type { ReactNode } from "react";
import { Link } from "@tanstack/react-router";

import styles from "./oldSchool.module.css";

export type Crumb = {
    label: string;
    /** Resolved path to link to. Omit for the current (non-link) crumb. */
    to?: string;
};

/**
 * Retro Vieweb-style window frame: a blue title bar, a breadcrumb strip,
 * an optional grey toolbar, and a light body. Used by every Old-school
 * screen so the whole section keeps the classic look. Styling lives in
 * {@link ./oldSchool.module.css}; content is Mantine as usual.
 */
export function VwbFrame(props: {
    title: string;
    crumbs?: Crumb[];
    toolbar?: ReactNode;
    children: ReactNode;
}) {
    const { title, crumbs, toolbar, children } = props;
    return (
        <div className={styles.frame}>
            <div className={styles.titleBar}>
                <span>{title}</span>
            </div>
            {crumbs && crumbs.length > 0 ? (
                <div className={styles.breadcrumb} data-testid="vwb-breadcrumb">
                    {crumbs.map((c, i) => (
                        <span key={`${c.label}-${i}`} style={{ display: "inline-flex", gap: 4 }}>
                            {i > 0 ? <span className={styles.crumbSep}>{">"}</span> : null}
                            {c.to ? (
                                <Link
                                    to={c.to}
                                    className={styles.crumbLink}
                                >
                                    {c.label}
                                </Link>
                            ) : (
                                <span>{c.label}</span>
                            )}
                        </span>
                    ))}
                </div>
            ) : null}
            {toolbar ? <div className={styles.toolbar}>{toolbar}</div> : null}
            <div className={styles.body}>{children}</div>
        </div>
    );
}

/** A red-headed section box, matching the Vieweb entity/property panels. */
export function VwbSection(props: { heading: string; children: ReactNode }) {
    return (
        <div className={styles.section}>
            <h3 className={styles.sectionHeading}>{props.heading}</h3>
            <div className={styles.sectionBody}>{props.children}</div>
        </div>
    );
}

export { styles as oldSchoolStyles };
