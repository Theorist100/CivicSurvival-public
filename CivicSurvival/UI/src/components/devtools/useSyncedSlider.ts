import { useEffect, useRef, useState } from "react";

export function useSyncedSlider(rawValue: number, isDragging: boolean, sourceVersion: number = rawValue): [number, (value: number) => void] {
    const [value, setValue] = useState(rawValue);
    const lastSyncedSourceVersionRef = useRef(sourceVersion);

    useEffect(() => {
        if (isDragging) return;
        if (sourceVersion === lastSyncedSourceVersionRef.current) return;

        lastSyncedSourceVersionRef.current = sourceVersion;
        setValue(rawValue);
    }, [rawValue, isDragging, sourceVersion]);

    return [value, setValue];
}
