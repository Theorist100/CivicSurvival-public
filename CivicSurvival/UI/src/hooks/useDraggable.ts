/**
 * useDraggable - Hook for making elements draggable.
 *
 * Attach the returned `dragRef` to the element (callback ref). The hook reacts
 * to the element actually mounting, so it can measure the REAL size — not a
 * fallback. When no `initialPosition` is given the element is centered on its
 * real size (and re-centered on size changes) until the user drags it; when
 * an `initialPosition` is given that position is respected as-is.
 */

import { useState, useCallback, useEffect, useLayoutEffect, useRef, useMemo } from "react";

interface Position {
    x: number;
    y: number;
}

interface UseDraggableResult {
    position: Position;
    isDragging: boolean;
    handleMouseDown: (e: React.MouseEvent) => void;
    /** Callback ref — put it on the draggable element instead of your own ref. */
    dragRef: (el: HTMLElement | null) => void;
    /**
     * False until the user grabs the window. While false the consumer should let LAYOUT
     * place it (a flex-centered overlay, the way every modal in this project is centred)
     * and ignore `position`; the hook keeps tracking where layout actually put it, so
     * switching to absolute coordinates on grab happens without a jump.
     *
     * Computing a centre by hand cannot match layout: the size is only known after the
     * first frame, it changes with the content, and a window taller than the viewport
     * ends up hanging off the bottom where nothing can scroll to it. Layout has none of
     * those problems, so it owns the default placement and the hook owns only dragging.
     */
    hasMoved: boolean;
}

interface UseDraggableOptions {
    initialPosition?: Position;
}

const MIN_VISIBLE_MARGIN = 60;
const DEFAULT_ELEMENT_SIZE = 500;

function getViewport() {
    return { width: window.innerWidth, height: window.innerHeight };
}

// Pure clamp: takes the element size as values so that drag/resize handlers never
// query layout. getBoundingClientRect() from a mousemove handler races the cohtml
// layout thread — native null-deref crash class.
//
// Vertical rule differs from horizontal on purpose. Sideways a window may hang off
// the edge as long as a strip stays grabbable — that is how dragging one aside works.
// Downward it may not: a window whose bottom leaves the screen takes its content with
// it and there is no way to scroll the viewport to reach it. So the top is pulled up
// far enough for the whole window to fit, and only a window taller than the viewport
// sits at the top edge (its own inner scroll then covers the rest).
function clampPosition(raw: Position, elementWidth: number, elementHeight: number): Position {
    const { width, height } = getViewport();
    const maxTop = Math.max(0, height - elementHeight);
    return {
        x: Math.max(MIN_VISIBLE_MARGIN - elementWidth, Math.min(raw.x, width - MIN_VISIBLE_MARGIN)),
        y: Math.max(0, Math.min(raw.y, maxTop)),
    };
}

export function useDraggable(options: UseDraggableOptions | Position = {}): UseDraggableResult {
    const initialPosition = "x" in options ? options : options.initialPosition;

    const [node, setNode] = useState<HTMLElement | null>(null);
    const dragRef = useCallback((el: HTMLElement | null) => setNode(el), []);

    const [position, setPosition] = useState<Position>(() => clampPosition(
        initialPosition ?? {
            x: Math.max(0, (getViewport().width - DEFAULT_ELEMENT_SIZE) / 2),
            y: Math.max(0, (getViewport().height - DEFAULT_ELEMENT_SIZE) / 2),
        },
        DEFAULT_ELEMENT_SIZE,
        DEFAULT_ELEMENT_SIZE,
    ));
    const [isDragging, setIsDragging] = useState(false);
    const [hasMoved, setHasMoved] = useState(false);
    const dragOffset = useRef<Position>({ x: 0, y: 0 });
    const positionRef = useRef<Position>(position);
    const userMovedRef = useRef(false);
    // Element size cache — written only from the mount-time measurement and the
    // ResizeObserver below; read by drag/resize handlers instead of live layout.
    const sizeRef = useRef<{ width: number; height: number }>({
        width: DEFAULT_ELEMENT_SIZE,
        height: DEFAULT_ELEMENT_SIZE,
    });

    // Value-equal bail-out: clampPosition always returns a fresh object, so
    // without this an unchanged position would still re-render (and, under a
    // no-deps effect, loop). Returning prev keeps React from re-rendering.
    //
    // positionRef is written HERE and in the measure pass below — never from an effect
    // mirroring `position`. Such an effect runs AFTER useLayoutEffect in the same commit,
    // so on mount it overwrote the freshly measured layout rect with the pre-measurement
    // guess (a viewport-centred DEFAULT_ELEMENT_SIZE box). The first grab then read the
    // guess and jumped the window there — the exact jump hasMoved exists to prevent, and
    // it healed only if a ResizeObserver callback happened to land first.
    const applyPosition = useCallback((next: Position) => {
        positionRef.current = next;
        setPosition(prev => (prev.x === next.x && prev.y === next.y) ? prev : next);
    }, []);

    const handleMouseDown = useCallback((e: React.MouseEvent) => {
        const target = e.target as HTMLElement;
        if (target.tagName === "BUTTON" || target.tagName === "INPUT" || target.closest("button") || target.closest("[data-no-drag]")) {
            return;
        }
        userMovedRef.current = true;
        // Hand placement over from layout to coordinates. positionRef already holds where
        // layout put the window (kept current by the measure pass), so the switch lands on
        // the same pixels the player is looking at — no jump on grab.
        setHasMoved(true);
        applyPosition(positionRef.current);
        setIsDragging(true);
        dragOffset.current = {
            x: e.clientX - positionRef.current.x,
            y: e.clientY - positionRef.current.y,
        };
        e.preventDefault();
    }, [applyPosition]);

    useEffect(() => {
        if (!isDragging) return;
        const handleMouseMove = (e: MouseEvent) => {
            applyPosition(clampPosition({
                x: e.clientX - dragOffset.current.x,
                y: e.clientY - dragOffset.current.y,
            }, sizeRef.current.width, sizeRef.current.height));
        };
        const handleMouseUp = () => setIsDragging(false);
        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);
        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, [isDragging, applyPosition]);

    useEffect(() => {
        const handleResize = () => applyPosition(clampPosition(positionRef.current, sizeRef.current.width, sizeRef.current.height));
        window.addEventListener("resize", handleResize);
        return () => window.removeEventListener("resize", handleResize);
    }, [applyPosition]);

    // Size + live position tracking. Runs once per element mount/unmount (deps: [node]) —
    // NOT per render — so the ResizeObserver is created once. The layout reads here are the
    // only allowed ones: mount time and ResizeObserver callbacks run outside event dispatch,
    // when the tree is settled (a read from a mouse handler races the cohtml layout thread).
    //
    // While the window is still laid out by its container (hasMoved false), this keeps
    // positionRef in sync with where layout actually put it, so grabbing it switches to
    // coordinates seamlessly. Hand-computed centring used to live here and was wrong three
    // ways: the size is unknown on the first frame, it changes with the content, and a window
    // taller than the viewport ended up hanging off the bottom with nothing able to scroll to
    // it. An `initialPosition` consumer (DraggableWindow) still gets coordinates from the
    // start and is untouched by this.
    useLayoutEffect(() => {
        if (!node) {
            userMovedRef.current = false;
            setHasMoved(false);
            return;
        }

        const measure = () => {
            sizeRef.current = { width: node.offsetWidth, height: node.offsetHeight };

            if (!userMovedRef.current && !initialPosition) {
                // Layout owns placement — record it rather than override it.
                const rect = node.getBoundingClientRect();
                positionRef.current = { x: rect.left, y: rect.top };
                return;
            }

            // Under coordinates: hold position, pull back only if the new size pushed the
            // window off-screen.
            applyPosition(clampPosition(positionRef.current, sizeRef.current.width, sizeRef.current.height));
        };

        measure();
        const ro = new ResizeObserver(measure);
        ro.observe(node);
        return () => ro.disconnect();
    }, [node, initialPosition, applyPosition]);

    return useMemo(
        () => ({ position, isDragging, handleMouseDown, dragRef, hasMoved }),
        [position, isDragging, handleMouseDown, dragRef, hasMoved],
    );
}
