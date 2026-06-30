import { useEffect, useState } from "react";

const MS_PER_MINUTE = 60_000;

export function useNowMinute(): number {
    const [nowMinute, setNowMinute] = useState(() => Math.floor(Date.now() / MS_PER_MINUTE));

    useEffect(() => {
        const id = window.setInterval(() => {
            setNowMinute(Math.floor(Date.now() / MS_PER_MINUTE));
        }, MS_PER_MINUTE);

        return () => window.clearInterval(id);
    }, []);

    return nowMinute;
}
