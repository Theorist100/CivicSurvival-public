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
}

interface UseDraggableOptions {
    initialPosition?: Position;
}

const MIN_VISIBLE_MARGIN = 60;
const DEFAULT_ELEMENT_SIZE = 500;

function getViewport() {
    return { width: window.innerWidth, height: window.innerHeight };
}

// Pure clamp: takes the element width as a value so that drag/resize handlers
// never query layout. getBoundingClientRect() from a mousemove handler races
// the cohtml layout thread — native null-deref crash class.
function clampPosition(raw: Position, elementWidth: number): Position {
    const { width, height } = getViewport();
    return {
        x: Math.max(MIN_VISIBLE_MARGIN - elementWidth, Math.min(raw.x, width - MIN_VISIBLE_MARGIN)),
        y: Math.max(0, Math.min(raw.y, height - MIN_VISIBLE_MARGIN)),
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
    ));
    const [isDragging, setIsDragging] = useState(false);
    const dragOffset = useRef<Position>({ x: 0, y: 0 });
    const positionRef = useRef<Position>(position);
    const userMovedRef = useRef(false);
    // Element size cache — written only from the mount-time measurement and the
    // ResizeObserver below; read by drag/resize handlers instead of live layout.
    const sizeRef = useRef<{ width: number; height: number }>({
        width: DEFAULT_ELEMENT_SIZE,
        height: DEFAULT_ELEMENT_SIZE,
    });

    useEffect(() => { positionRef.current = position; }, [position]);

    // Value-equal bail-out: clampPosition always returns a fresh object, so
    // without this an unchanged position would still re-render (and, under a
    // no-deps effect, loop). Returning prev keeps React from re-rendering.
    const applyPosition = useCallback((next: Position) => {
        setPosition(prev => (prev.x === next.x && prev.y === next.y) ? prev : next);
    }, []);

    const handleMouseDown = useCallback((e: React.MouseEvent) => {
        const target = e.target as HTMLElement;
        if (target.tagName === "BUTTON" || target.tagName === "INPUT" || target.closest("button") || target.closest("[data-no-drag]")) {
            return;
        }
        userMovedRef.current = true;
        setIsDragging(true);
        dragOffset.current = {
            x: e.clientX - positionRef.current.x,
            y: e.clientY - positionRef.current.y,
        };
        e.preventDefault();
    }, []);

    useEffect(() => {
        if (!isDragging) return;
        const handleMouseMove = (e: MouseEvent) => {
            applyPosition(clampPosition({
                x: e.clientX - dragOffset.current.x,
                y: e.clientY - dragOffset.current.y,
            }, sizeRef.current.width));
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
        const handleResize = () => applyPosition(clampPosition(positionRef.current, sizeRef.current.width));
        window.addEventListener("resize", handleResize);
        return () => window.removeEventListener("resize", handleResize);
    }, [applyPosition]);

    // Size tracking + source-correct centering. Runs once per element
    // mount/unmount (deps: [node]) — NOT per render — so the ResizeObserver
    // is created once. The offsetWidth/offsetHeight read here is the single
    // allowed layout measurement: mount time and ResizeObserver callbacks run
    // outside event dispatch, when the tree is settled. Centering applies only
    // when no explicit initialPosition was requested (DraggableWindow passes
    // one and must keep it; HelpSection does not and wants center). userMoved
    // disables it after a drag; unmount resets the flag so the next mount
    // re-centers. No consumer has to call anything.
    useLayoutEffect(() => {
        if (!node) {
            userMovedRef.current = false;
            return;
        }
        const measureAndCenter = () => {
            sizeRef.current = { width: node.offsetWidth, height: node.offsetHeight };
            if (initialPosition || userMovedRef.current) return;
            const { width, height } = getViewport();
            applyPosition(clampPosition({
                x: Math.max(0, (width - sizeRef.current.width) / 2),
                y: Math.max(0, (height - sizeRef.current.height) / 2),
            }, sizeRef.current.width));
        };
        measureAndCenter();
        const ro = new ResizeObserver(measureAndCenter);
        ro.observe(node);
        return () => ro.disconnect();
    }, [node, initialPosition, applyPosition]);

    return useMemo(
        () => ({ position, isDragging, handleMouseDown, dragRef }),
        [position, isDragging, handleMouseDown, dragRef],
    );
}
