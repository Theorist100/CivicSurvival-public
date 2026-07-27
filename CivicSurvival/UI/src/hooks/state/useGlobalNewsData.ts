/**
 * Enriched global news data.
 *
 * Pulls raw GlobalNews state + parses newsFeed$, attaches NewsCategory
 * to each official news item, exposes derived flags. HeraldSection (and any
 * future news view) consumes a single hook instead of recomputing
 * mood→category mapping in render.
 */

import { useMemo } from "react";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "./useGlobalNews";
import { useSafeString } from "../useSafeBinding";
import { bindingDataOrDefault } from "../domain";
import { newsFeed$ } from "../bindings";
import { type NewsPost, type SocialMood, parseNewsFeed } from "../../components/news/sections/newsUtils";
import { type TranslationKey } from "../../locales";
import { SocialMoodValues } from "../../types/sharedEnums.generated";
import { assertNever } from "../../utils/exhaustive";

// News category mood colors (mood → palette mapping).
const MOOD_BREAKING = "#cc3333";
const MOOD_WITNESS = "#5588aa";
const MOOD_RUMOR = "#8a7a5a";
const MOOD_OFFICIAL = "#888870";
const MOOD_DISPATCH = "#666666";

export interface NewsCategory {
    labelKey: TranslationKey;
    color: string;
    icon: string;
}

export interface CategorizedPost extends NewsPost {
    category: NewsCategory;
}

function getNewsCategory(mood: SocialMood): NewsCategory {
    switch (mood) {
        case "Warning":
        case "Angry":
            return { labelKey: "UI_HERALD_CAT_BREAKING", color: MOOD_BREAKING, icon: "!" };
        case "Suffering":
            return { labelKey: "UI_HERALD_CAT_WITNESS", color: MOOD_WITNESS, icon: "\"" };
        case "Suspicious":
        case "Paranoid":
            return { labelKey: "UI_HERALD_CAT_RUMOR", color: MOOD_RUMOR, icon: "?" };
        case "Smug":
            return { labelKey: "UI_HERALD_CAT_OFFICIAL", color: MOOD_OFFICIAL, icon: "*" };
        case "Neutral":
            return { labelKey: "UI_HERALD_CAT_DISPATCH", color: MOOD_DISPATCH, icon: "-" };
        default:
            return assertNever(mood, "useGlobalNewsData.mood");
    }
}

// Module-level: same object reference reused across renders.
const CATEGORY_BY_MOOD: Record<SocialMood, NewsCategory> = SocialMoodValues.reduce<Record<SocialMood, NewsCategory>>((acc, mood) => {
    acc[mood] = getNewsCategory(mood);
    return acc;
}, {} as Record<SocialMood, NewsCategory>);

export function useGlobalNewsData() {
    const news = useGlobalNews();
    const newsFeedJson = useSafeString(newsFeed$, "[]");

    return useMemo(() => {
        const readyNews = bindingDataOrDefault(news, DEFAULT_GLOBAL_NEWS_STATE);
        const allPosts = parseNewsFeed(newsFeedJson);
        const posts: CategorizedPost[] = allPosts.map(p => ({
            ...p,
            category: CATEGORY_BY_MOOD[p.mood],
        }));
        const firstPost = posts[0];
        const hasBreaking = firstPost?.mood === "Warning" || firstPost?.mood === "Angry";

        return {
            news: readyNews,
            posts,
            postsCount: posts.length,
            hasBreaking,
            breakingMoodColor: MOOD_BREAKING,
            categoryByMood: CATEGORY_BY_MOOD,
        };
    }, [news, newsFeedJson]);
}
