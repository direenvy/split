import type { AddExpenseRequest, CreateGroupRequest, Group } from './types'

/*
  The only place in the client that talks to the network.

  Note the URLs: plain `/api/...`, with no host and no base URL from an environment variable.
  The ASP.NET Core host serves this bundle, so the API is always on the same origin as the
  page. That is the single-deployable choice paying off — there is nothing here to configure
  per environment and nothing to get wrong between development and production.

  Note also what this file does NOT do: any arithmetic. Every amount that matters is computed
  on the server in decimal. The client receives numbers, formats them, and sends back exactly
  what the user typed. JavaScript numbers are IEEE doubles, so 0.1 + 0.2 is 0.30000000000000004
  — fine for display, catastrophic for a ledger.
*/

export class ApiError extends Error {
  // Written out longhand rather than as a constructor parameter property, because this
  // project builds with `erasableSyntaxOnly`: TypeScript may only add types that vanish when
  // they are stripped, and `constructor(public readonly status: number)` emits real code.
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...init?.headers,
      },
    })
  } catch {
    // fetch only rejects when the request never completed: offline, DNS, connection refused.
    throw new ApiError('Could not reach the server. Is it running?', 0)
  }

  if (!response.ok) {
    // The API reports problems as { "error": "..." }. Fall back to the status text if some
    // layer below it (a proxy, say) returned something else.
    const body = await response.json().catch(() => null)
    const message =
      body && typeof body.error === 'string'
        ? body.error
        : `The server returned ${response.status}.`
    throw new ApiError(message, response.status)
  }

  return (await response.json()) as T
}

export const api = {
  createGroup(body: CreateGroupRequest): Promise<Group> {
    return request<Group>('/api/groups', { method: 'POST', body: JSON.stringify(body) })
  },

  getGroup(slug: string): Promise<Group> {
    return request<Group>(`/api/groups/${encodeURIComponent(slug)}`)
  },

  addMember(slug: string, name: string): Promise<Group> {
    return request<Group>(`/api/groups/${encodeURIComponent(slug)}/members`, {
      method: 'POST',
      body: JSON.stringify({ name }),
    })
  },

  addExpense(slug: string, body: AddExpenseRequest): Promise<Group> {
    return request<Group>(`/api/groups/${encodeURIComponent(slug)}/expenses`, {
      method: 'POST',
      body: JSON.stringify(body),
    })
  },

  deleteExpense(slug: string, expenseId: string): Promise<Group> {
    return request<Group>(
      `/api/groups/${encodeURIComponent(slug)}/expenses/${encodeURIComponent(expenseId)}`,
      { method: 'DELETE' },
    )
  },
}
