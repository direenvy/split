// The shapes the API returns, mirroring the records in src/Split.Api/Contracts.cs.
//
// These are hand-written rather than generated. The API has five endpoints and one real
// payload, so a code generator would be more moving parts than it saves — but if the contract
// grew, generating this file from the OpenAPI document is the obvious next step.

export interface Member {
  id: string
  name: string
}

export interface Share {
  participantId: string
  amount: number
}

export interface Expense {
  id: string
  description: string
  amount: number
  paidBy: string
  createdAt: string
  shares: Share[]
}

export interface Balance {
  participantId: string
  net: number
}

export interface Payment {
  from: string
  to: string
  amount: number
}

export interface Group {
  slug: string
  name: string
  currency: string
  createdAt: string
  members: Member[]
  expenses: Expense[]
  totalSpend: number
  balances: Balance[]
  settlement: Payment[]
}

export interface CurrentUser {
  id: string
  displayName: string
  email: string
  avatarUrl: string | null
}

export interface MeResponse {
  signInAvailable: boolean
  user: CurrentUser | null
}

/** One row in the signed-in user's history. */
export interface GroupSummary {
  slug: string
  name: string
  currency: string
  isCreator: boolean
  lastSeenAt: string
  people: number
  totalSpend: number
}

export interface PreviewBalance {
  name: string
  net: number
}

export interface PreviewPayment {
  from: string
  to: string
  amount: number
}

export interface SettlementPreview {
  payments: PreviewPayment[]
  /** What settling the obvious way would have cost: every debtor paying every creditor. */
  naivePaymentCount: number
}

export interface CreateGroupRequest {
  name: string
  currency: string
  members: string[]
}

export interface AddExpenseRequest {
  description: string
  amount: number
  paidBy: string
  splitAmong: string[]
}
