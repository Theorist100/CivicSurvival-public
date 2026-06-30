type SearchParamsEntry = [string, string];

type SearchParamsLike = {
    forEach(callback: (value: unknown, key: string) => void): void;
};

type SearchParamsInit =
    | string
    | Iterable<readonly [unknown, unknown]>
    | Record<string, unknown>
    | SearchParamsLike
    | null
    | undefined;

class CoherentURLSearchParams {
    private readonly entriesList: SearchParamsEntry[] = [];

    public constructor(init?: SearchParamsInit) {
        if (init == null) return;

        if (typeof init === "string") {
            this.readString(init);
            return;
        }

        if (isIterableEntries(init)) {
            for (const [key, value] of init) {
                this.append(String(key), String(value));
            }
            return;
        }

        if (isSearchParamsLike(init)) {
            init.forEach((value, key) => this.append(key, String(value)));
            return;
        }

        for (const [key, value] of Object.entries(init)) {
            this.append(key, String(value));
        }
    }

    public append(name: string, value: string): void {
        this.entriesList.push([String(name), String(value)]);
    }

    public delete(name: string): void {
        const key = String(name);
        for (let index = this.entriesList.length - 1; index >= 0; index--) {
            if (this.entriesList[index]?.[0] === key) {
                this.entriesList.splice(index, 1);
            }
        }
    }

    public get(name: string): string | null {
        const key = String(name);
        const item = this.entriesList.find(([entryKey]) => entryKey === key);
        return item?.[1] ?? null;
    }

    public getAll(name: string): string[] {
        const key = String(name);
        return this.entriesList.filter(([entryKey]) => entryKey === key).map(([, value]) => value);
    }

    public has(name: string): boolean {
        const key = String(name);
        return this.entriesList.some(([entryKey]) => entryKey === key);
    }

    public set(name: string, value: string): void {
        const key = String(name);
        this.delete(key);
        this.append(key, value);
    }

    public sort(): void {
        this.entriesList.sort(([left], [right]) => left.localeCompare(right));
    }

    public forEach(callback: (value: string, key: string, parent: CoherentURLSearchParams) => void): void {
        for (const [key, value] of this.entriesList) {
            callback(value, key, this);
        }
    }

    public keys(): IterableIterator<string> {
        return this.entriesList.map(([key]) => key)[Symbol.iterator]();
    }

    public values(): IterableIterator<string> {
        return this.entriesList.map(([, value]) => value)[Symbol.iterator]();
    }

    public entries(): IterableIterator<SearchParamsEntry> {
        return this.entriesList.map(([key, value]) => [key, value] satisfies SearchParamsEntry)[Symbol.iterator]();
    }

    public [Symbol.iterator](): IterableIterator<SearchParamsEntry> {
        return this.entries();
    }

    public toString(): string {
        return this.entriesList
            .map(([key, value]) => `${encodeQueryPart(key)}=${encodeQueryPart(value)}`)
            .join("&");
    }

    private readString(value: string): void {
        const query = value.startsWith("?") ? value.slice(1) : value;
        if (query.length === 0) return;

        for (const part of query.split("&")) {
            if (part.length === 0) continue;

            const separator = part.indexOf("=");
            if (separator === -1) {
                this.append(decodeQueryPart(part), "");
                continue;
            }

            this.append(decodeQueryPart(part.slice(0, separator)), decodeQueryPart(part.slice(separator + 1)));
        }
    }
}

export function ensureUrlSearchParamsCompat(): void {
    if (typeof globalThis.URLSearchParams !== "undefined") return;

    const globalWithPolyfill = globalThis as typeof globalThis & {
        URLSearchParams: typeof URLSearchParams;
    };
    globalWithPolyfill.URLSearchParams = CoherentURLSearchParams as unknown as typeof URLSearchParams;
}

function isSearchParamsLike(value: object): value is SearchParamsLike {
    return "forEach" in value && typeof value.forEach === "function";
}

function isIterableEntries(value: object): value is Iterable<readonly [unknown, unknown]> {
    return Symbol.iterator in value && typeof value[Symbol.iterator] === "function";
}

function encodeQueryPart(value: string): string {
    return encodeURIComponent(value).replace(/%20/g, "+");
}

function decodeQueryPart(value: string): string {
    return decodeURIComponent(value.replace(/\+/g, "%20"));
}
