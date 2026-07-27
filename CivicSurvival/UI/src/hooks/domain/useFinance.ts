/**
 * Finance domain hook.
 */

import { financeState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_FINANCE_DTO, isFinanceDto } from "../../types/domainDtos";

export const useFinance = () =>
    useDtoBinding(financeState$, isFinanceDto, { debugName: "financeState", defaultValue: DEFAULT_FINANCE_DTO });

export const useFinanceState = useFinance;
