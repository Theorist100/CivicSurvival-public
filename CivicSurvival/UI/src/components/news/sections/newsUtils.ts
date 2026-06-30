/**
 * Shared types and utilities for NEWS domain sections (Chipper + Herald)
 */

import { isSocialMood, type SocialMood } from "../../../types/sharedEnums.generated";
import {
    isNewsPostDto,
    isSocialPostDto,
    type NewsPostDto,
    type SocialPostDto,
} from "../../../types/domainDtos.generated";
import { safeJsonParse } from "../../../utils/jsonParse";
export { formatTimeAgo } from "./newsTime";

export type { SocialMood };

export interface SocialPost {
    author: string;
    authorName: string;
    message: string;
    mood: SocialMood;
    timestamp: number;
    isOfficial: boolean;
}

export interface NewsPost {
    id: string;
    source: string;
    title: string;
    body: string;
    mood: SocialMood;
    timestamp: number;
    rawCategory: string;
    isAiGenerated: boolean;
}

const toSocialPost = (dto: SocialPostDto): SocialPost | null => {
    if (!isSocialMood(dto.Mood)) return null;
    return {
        author: dto.Author,
        authorName: dto.AuthorName,
        message: dto.Message,
        mood: dto.Mood,
        timestamp: dto.Timestamp,
        isOfficial: dto.IsOfficial,
    };
};

const toNewsPost = (dto: NewsPostDto): NewsPost | null => {
    if (!isSocialMood(dto.Mood)) return null;
    return {
        id: dto.PostId,
        source: dto.Source,
        title: dto.Title,
        body: dto.Body,
        mood: dto.Mood,
        timestamp: dto.Timestamp,
        rawCategory: dto.Category,
        isAiGenerated: dto.IsAiGenerated,
    };
};

export const parseSocialFeed = (json: string): SocialPost[] => {
    const parsed = safeJsonParse(json, Array.isArray);
    if (!parsed) return [];
    const result: SocialPost[] = [];
    for (const item of parsed) {
        if (!isSocialPostDto(item)) continue;
        const post = toSocialPost(item);
        if (post !== null) result.push(post);
    }
    return result;
};

export const parseNewsFeed = (json: string): NewsPost[] => {
    const parsed = safeJsonParse(json, Array.isArray);
    if (!parsed) return [];
    const result: NewsPost[] = [];
    for (const item of parsed) {
        if (!isNewsPostDto(item)) continue;
        const post = toNewsPost(item);
        if (post !== null) result.push(post);
    }
    return result;
};
