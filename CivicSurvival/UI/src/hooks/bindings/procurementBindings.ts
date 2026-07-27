/**
 * Procurement Bindings - Phase B corruption mechanics
 * Triggers (with entityIndex) and parse helpers.
 * Wire types are generated from ui-dto.contract.yaml as PendingProcurementOfferEntry
 * and ActiveContractEntry; this file maps them to consumer-facing camelCase shapes.
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";
import {
    isActiveContractEntry,
    isPendingProcurementOfferEntry,
    type ActiveContractEntry,
    type PendingProcurementOfferEntry,
} from "../../types/domainDtos.generated";
import { asEntityIndex, asEntityVersion, type EntityIndex, type EntityVersion } from "../../types/semantic";

// ============================================================================
// Types
// ============================================================================

export type ProcurementService = "Electricity" | "Roads" | "Water" | "Healthcare" | "Fire" | "Education" | "Garbage";
export type ProcurementContractType = "Maintenance" | "Supply";

export interface ProcurementOffer {
    entityIndex: EntityIndex;
    entityVersion: EntityVersion;
    service: ProcurementService;
    contractType: ProcurementContractType;
    officialVendorName: string;
    shadyVendorName: string;
    officialPrice: number;
    shadyPrice: number;
    kickbackOffer: number;
    officialQuality: number;
    shadyQuality: number;
    canAcceptShady: boolean;
    acceptShadyLockedReasonId: string;
    acceptShadyEffectiveCost: number;
    buildingName: string;
}

export interface ActiveContract {
    entityIndex: EntityIndex;
    buildingName: string;
    contractType: ProcurementContractType;
    vendorName: string;
    quality: number;
    kickbackAmount: number;
    isShady: boolean;
    daysRemaining: number;
}

// ============================================================================
// Actions
// ============================================================================

/**
 * Accept official (expensive, reliable) contract.
 * @param entityIndex Building entity index
 * @param entityVersion Building entity version
 */
export const acceptOfficialContract = (entityIndex: EntityIndex, entityVersion: EntityVersion, expectedPrice: number): void => {
    triggerCivic(B.AcceptOfficialContract, entityIndex, entityVersion, expectedPrice);
};

/**
 * Accept shady (cheap + kickback, unreliable) contract.
 * @param entityIndex Building entity index
 * @param entityVersion Building entity version
 */
export const acceptShadyContract = (entityIndex: EntityIndex, entityVersion: EntityVersion, expectedPrice: number): void => {
    triggerCivic(B.AcceptShadyContract, entityIndex, entityVersion, expectedPrice);
};

/**
 * Decline the procurement offer.
 * @param entityIndex Building entity index
 * @param entityVersion Building entity version
 */
export const declineProcurement = (entityIndex: EntityIndex, entityVersion: EntityVersion): void => {
    triggerCivic(B.DeclineProcurement, entityIndex, entityVersion);
};

// ============================================================================
// Helpers
// ============================================================================

const normalizeProcurementOffer = (offer: PendingProcurementOfferEntry): ProcurementOffer | null => {
    if (!isProcurementService(offer.Service) || !isProcurementContractType(offer.ContractType))
        return null;
    return {
        entityIndex: asEntityIndex(offer.EntityIndex),
        entityVersion: asEntityVersion(offer.EntityVersion),
        service: offer.Service,
        contractType: offer.ContractType,
        officialVendorName: offer.OfficialVendorName,
        shadyVendorName: offer.ShadyVendorName,
        officialPrice: offer.OfficialPrice,
        shadyPrice: offer.ShadyPrice,
        kickbackOffer: offer.KickbackOffer,
        officialQuality: offer.OfficialQuality,
        shadyQuality: offer.ShadyQuality,
        canAcceptShady: offer.CanAcceptShady,
        acceptShadyLockedReasonId: offer.AcceptShadyLockedReasonId,
        acceptShadyEffectiveCost: offer.AcceptShadyEffectiveCost,
        buildingName: offer.BuildingName,
    };
};

/**
 * Parse procurement offer from the typed wire entry.
 * Returns null if absent or the entry's enum-shaped fields do not validate.
 */
export const parseProcurementOffer = (value: unknown): ProcurementOffer | null => {
    if (!isPendingProcurementOfferEntry(value)) return null;
    return normalizeProcurementOffer(value);
};

const normalizeActiveContract = (entry: ActiveContractEntry): ActiveContract | null => {
    if (!isProcurementContractType(entry.ContractType)) return null;
    return {
        entityIndex: asEntityIndex(entry.EntityIndex),
        buildingName: entry.BuildingName,
        contractType: entry.ContractType,
        vendorName: entry.VendorName,
        quality: entry.Quality,
        kickbackAmount: entry.KickbackAmount,
        isShady: entry.IsShady,
        daysRemaining: entry.DaysRemaining,
    };
};

/**
 * Parse active contracts from the typed wire array. Entries whose enum-shaped
 * fields do not validate are silently dropped.
 */
export const parseActiveContracts = (value: unknown): ActiveContract[] => {
    if (!Array.isArray(value)) return [];
    const result: ActiveContract[] = [];
    for (const item of value) {
        if (!isActiveContractEntry(item)) continue;
        const mapped = normalizeActiveContract(item);
        if (mapped !== null) result.push(mapped);
    }
    return result;
};

const isProcurementService = (value: unknown): value is ProcurementService =>
    value === "Electricity"
    || value === "Roads"
    || value === "Water"
    || value === "Healthcare"
    || value === "Fire"
    || value === "Education"
    || value === "Garbage";

const isProcurementContractType = (value: unknown): value is ProcurementContractType =>
    value === "Maintenance" || value === "Supply";
