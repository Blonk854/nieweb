export function formatApplicableMetric(
    applicable: boolean,
    value: number,
    format: (n: number) => string,
    na: string,
): string {
    return applicable ? format(value) : na;
}
