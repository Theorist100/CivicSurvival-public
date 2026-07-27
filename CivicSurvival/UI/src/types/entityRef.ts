export type EntityRef = {
    index: number;
    version: number;
};

export const entityRefKey = (entity: EntityRef): string => `${entity.index}:${entity.version}`;
