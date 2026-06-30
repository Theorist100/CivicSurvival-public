const ID = "civic-mod-root";

export const CIVIC_MOD_LAYER = 9999;

let root: HTMLElement | null = null;

export const getModRoot = (): HTMLElement => {
    const existing = document.getElementById(ID);
    if (existing?.isConnected) {
        root = existing;
        return existing;
    }

    if (root?.isConnected) return root;

    root = document.createElement("div");
    root.id = ID;
    root.style.position = "relative";
    root.style.zIndex = String(CIVIC_MOD_LAYER);
    root.style.isolation = "isolate";
    document.body.appendChild(root);
    return root;
};
