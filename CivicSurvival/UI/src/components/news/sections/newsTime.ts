type TimeAgoTranslationKey =
    | "UI_NEWS_TIME_JUST_NOW"
    | "UI_NEWS_TIME_MIN_AGO"
    | "UI_NEWS_TIME_H_AGO"
    | "UI_NEWS_TIME_D_AGO";

type TranslateFn = (key: TimeAgoTranslationKey, ...args: (string | number)[]) => string;

const SECONDS_PER_HOUR = 3600;
const SECONDS_PER_DAY = 86400;

export const formatTimeAgo = (timestamp: number, nowMinute: number, t: TranslateFn): string => {
    if (!timestamp || !Number.isFinite(timestamp)) return t("UI_NEWS_TIME_JUST_NOW");
    const now = nowMinute * 60;
    const postMinute = Math.floor(timestamp / 60) * 60;
    const diff = Math.max(0, now - postMinute);
    if (diff < 60) return t("UI_NEWS_TIME_JUST_NOW");
    if (diff < SECONDS_PER_HOUR) return t("UI_NEWS_TIME_MIN_AGO", Math.floor(diff / 60));
    if (diff < SECONDS_PER_DAY) return t("UI_NEWS_TIME_H_AGO", Math.floor(diff / SECONDS_PER_HOUR));
    return t("UI_NEWS_TIME_D_AGO", Math.floor(diff / SECONDS_PER_DAY));
};
