# InfraPilot Architecture Plan

## Purpose

This document defines the initial architecture for InfraPilot before implementation starts.

Goals:
- keep the MVP simple, local, and testable
- build around a single Windows agent with modular capabilities
- keep the central API as the control plane
- let the UI adapt to the capabilities reported by each agent

Non-goals for the MVP:
- Linux support
- cloud integrations
- multi-tenant features
- enterprise auth flows
- push-based command execution from central to servers

---

## Key product decisions

### 1. Execution model: pull, not push

InfraPilot will use an agent-driven execution model.

Flow:
1. A user triggers an action from the web UI.
2. The central API stores the action as pending.
3. The agent polls the central API for work.
4. The central API leases the next pending action to that agent.
5. The agent executes the action locally through the target capability.
6. The agent reports the final result back to the central API.

Why:
- avoids inbound connections from central to managed servers
- works better with firewalls, NAT, and isolated VM setups
- lets each agent control polling cadence and load
- keeps the central API from becoming a push orchestrator

Initial MVP rules:
- heartbeat and action polling are separate loops
- heartbeat every 30-60 seconds
- action polling every 5-10 seconds
- one concurrent action per agent in the first version
- action leases prevent duplicate execution

### 2. Capability-first architecture

InfraPilot is not an IIS product with extra modules and not an inventory product with remote actions attached later.

InfraPilot is:
- one agent core
- multiple independent capabilities
- one central API
- one UI that renders agent state according to available capabilities

Initial capabilities:
- `services`
- `scheduledTasks`
- `fileTree`
- `iis`

### 3. Central API is the system of record

The web UI must only talk to the central API.

The central API is responsible for:
- registration and approval
- heartbeat tracking
- capability catalog
- action queue
- action audit trail
- latest snapshots and capability state summaries

The UI must not call agents directly.

---

## Lessons from reference repositories

### IISentinel

Useful ideas:
- enrollment, heartbeat, command polling, and result reporting already prove the right execution direction
- `Microsoft.Web.Administration` is the correct base for IIS operations in Windows
- central action queue and action history are valid concepts

What not to carry forward:
- large `Program.cs` with transport, loops, execution, logging, and providers mixed together
- IIS-specific modeling at the center of the whole product
- UI patterns that assume a fixed IIS-oriented data model

### InventoryOpsMvp

Useful ideas:
- agent registration and approval workflow
- background loops with retry and non-overlap
- snapshot fingerprinting and "only if changed"
- filters and collectors for services, tasks, and filesystem

What not to carry forward:
- one fixed snapshot DTO for all domains
- central ingest flow tied to a monolithic inventory payload
- fragile task collection based on localized text parsing as the long-term design

### Reuse strategy

Reuse:
- proven logic and provider ideas
- interaction patterns
- operational safeguards like retry, leases, and deduplication

Rewrite:
- contracts
- solution structure
- capability boundaries
- central persistence model
- UI composition

Discard:
- direct UI to agent communication
- giant startup files
- hardcoded domain assumptions inside central models

---

## Proposed solution structure

```text
src/
  InfraPilot.Contracts
  InfraPilot.Agent.Core
  InfraPilot.Agent.Host.Windows
  InfraPilot.Capabilities.Abstractions
  InfraPilot.Capabilities.Services.Windows
  InfraPilot.Capabilities.ScheduledTasks.Windows
  InfraPilot.Capabilities.FileTree.Windows
  InfraPilot.Capabilities.Iis.Windows
  InfraPilot.Central.Application
  InfraPilot.Central.Api
  InfraPilot.Central.Infrastructure.Sqlite
  InfraPilot.Web

tests/
  InfraPilot.Agent.Tests
  InfraPilot.Central.Tests
  InfraPilot.Capabilities.Tests
  InfraPilot.Contracts.Tests
```

### Project roles

`InfraPilot.Contracts`
- DTOs and message contracts shared between agent and central
- no provider logic
- versioned carefully

`InfraPilot.Agent.Core`
- agent orchestration
- registration, heartbeat, polling, action execution pipeline
- capability loading and scheduling
- no Windows-specific implementation details

`InfraPilot.Agent.Host.Windows`
- Windows service host
- dependency injection bootstrapping
- appsettings, logging, and host-specific wiring

`InfraPilot.Capabilities.Abstractions`
- shared capability contracts
- capability descriptors
- action request and result abstractions

`InfraPilot.Capabilities.*.Windows`
- each module owns one operational domain
- OS provider access
- snapshot generation
- action execution

`InfraPilot.Central.Application`
- use cases and orchestration for central operations
- action queue rules
- lease handling
- agent lifecycle decisions

`InfraPilot.Central.Api`
- HTTP transport layer
- authentication entrypoints for the MVP
- endpoints mapped to application services

`InfraPilot.Central.Infrastructure.Sqlite`
- EF Core persistence
- database model
- repositories or persistence services

`InfraPilot.Web`
- central UI
- agent list, details, capability views, action launchers

---

## Agent design

### Core responsibilities

The agent core should own:
- persistent agent identity
- enrollment and approval awareness
- heartbeat loop
- action polling loop
- action execution pipeline
- snapshot publication loop
- local action and sync state

The agent core should not know how to enumerate services, IIS sites, or scheduled tasks directly.

### Internal building blocks

Suggested internal components:
- `IAgentIdentityStore`
- `IAgentRegistrationClient`
- `IAgentHeartbeatClient`
- `IAgentSnapshotPublisher`
- `IAgentActionPoller`
- `IAgentActionReporter`
- `ICapabilityRegistry`
- `ICapabilityModule`
- `IActionExecutor`

### Capability contract

Each capability should expose a common shape such as:
- metadata and key
- snapshot collection
- supported actions
- action execution

Conceptual interface:

```text
ICapabilityModule
  CapabilityKey
  Version
  Describe()
  CollectSnapshotAsync()
  ExecuteActionAsync()
```

Each capability should return:
- descriptor
- latest state payload
- action catalog

That gives the central API enough metadata to:
- know what each agent supports
- render capability-specific sections in the UI
- validate action routing

### Capability boundaries

`services`
- collect service inventory and state
- execute actions like start, stop, restart

`scheduledTasks`
- collect task metadata and state
- execute actions like run, enable, disable

`fileTree`
- expose configured roots only
- allow safe read-oriented exploration in the MVP
- avoid write operations in the first iteration

`iis`
- collect sites, app pools, bindings, and states
- execute app pool and site actions

### Agent scheduling

Recommended loops:
- registration check on startup
- heartbeat timer
- snapshot sync timer
- action poll timer

The action poll loop should:
- request work only when capacity is available
- receive one leased action
- report progress or completion
- release or timeout stale work safely

---

## Central API design

### Responsibilities

The central API is the control plane for all agents.

It should manage:
- enrollment and approval
- short-lived agent authentication for ongoing communication
- capability registration
- snapshot storage
- action queue and execution status
- audit trail and operational history

### Suggested endpoint families

`/api/agents`
- enroll
- approve or revoke
- list and detail
- heartbeat

`/api/agents/{agentId}/capabilities`
- publish active capabilities
- fetch latest capability catalog

`/api/agents/{agentId}/snapshots`
- publish snapshot batches or per-capability snapshots
- fetch latest snapshots

`/api/actions`
- create actions from UI
- list actions
- inspect action details

`/api/agents/{agentId}/actions/pull`
- agent polls for next leased action

`/api/actions/{actionId}/result`
- agent reports result

### Central application rules

Important rules:
- actions can only target a capability that the agent has published
- the central API owns action status transitions
- a leased action cannot be picked by a second agent
- stale leases must be recoverable
- results must be idempotent when possible

### Snapshot strategy

Do not force one giant system snapshot DTO.

Prefer:
- one envelope for agent snapshot upload
- one payload per capability inside it

Conceptually:

```text
AgentSnapshotEnvelope
  AgentId
  CollectedUtc
  CapabilitySnapshots[]

CapabilitySnapshot
  CapabilityKey
  SchemaVersion
  Hash
  PayloadJson
```

Benefits:
- central stays generic
- UI can evolve by capability
- new capabilities do not require redesigning the whole contract

---

## Persistence model

SQLite is the right starting point for the MVP.

Suggested tables:
- `Agents`
- `AgentAuthTokens` or `AgentSessions`
- `AgentCapabilities`
- `Heartbeats`
- `CapabilitySnapshots`
- `ActionCommands`
- `ActionExecutions` or execution fields on commands

### Minimum entity intent

`Agents`
- stable identity and approval status
- display name, machine name, version, last seen

`AgentCapabilities`
- capability key
- schema version
- enabled status
- last published time

`CapabilitySnapshots`
- agent id
- capability key
- collected time
- hash
- JSON payload
- optional summary fields for indexing

`ActionCommands`
- requested action
- target capability
- target resource
- payload
- status
- lease owner
- lease expiry
- audit fields

### Data modeling principle

Store domain payloads as JSON first, indexed summaries second.

That keeps the MVP flexible and avoids over-modeling `services`, `tasks`, `fileTree`, and `iis` before we know how the UI and workflows settle.

---

## Web UI design

### MVP screens

1. Agent list
- status
- last heartbeat
- enabled capabilities
- last action outcome

2. Agent detail
- identity and health
- dynamic capability tabs
- action history

3. Capability views
- `services`: service list and actions
- `scheduledTasks`: task list and actions
- `fileTree`: readonly tree explorer
- `iis`: sites, app pools, bindings, and actions

4. Action view
- pending, running, failed, succeeded
- timestamps, duration, error message

### UI composition model

The UI should support:
- a generic capability shell
- capability-specific renderers for the four MVP modules
- fallback renderer for unknown future capabilities

That gives us future growth without forcing a generic-only user experience.

---

## MVP operational model

### Security model for the MVP

Keep it simple:
- enrollment secret for first registration
- issued agent credential for normal communication after enrollment
- no inbound command execution from central
- no cloud dependencies

Avoid in MVP:
- complex user auth systems
- certificate infrastructure
- cross-tenant auth boundaries

### Safety rules for actions

First version rules:
- allowlisted action catalog only
- capability validates its own targets and parameters
- central records who requested the action
- agent returns structured result with message and error details
- file write or delete actions are out of scope for the first `fileTree` MVP

---

## Recommended implementation order

### Phase 0 - Architecture baseline

Deliverables:
- solution structure agreed
- shared terminology agreed
- initial contracts drafted
- persistence strategy agreed

### Phase 1 - Central and agent core skeleton

Deliverables:
- central API bootstrapped
- SQLite persistence bootstrapped
- Windows agent host bootstrapped
- agent identity, registration, approval, heartbeat
- action queue lifecycle without domain actions yet

Success criteria:
- one local agent can enroll
- central shows agent state
- central can create a test action
- agent can lease and complete a no-op test action

### Phase 2 - First capability pilot: services

Why first:
- broad operational value
- simpler than IIS
- validates both read and execute flows cleanly

Deliverables:
- services capability descriptor
- service snapshot collection
- service actions start, stop, restart
- UI renderer for services

Success criteria:
- UI can inspect services from a real agent
- user can trigger a service action
- action lifecycle is visible end to end

### Phase 3 - Scheduled tasks

Deliverables:
- scheduled task provider
- task snapshot model
- actions such as run, enable, disable if safe
- UI renderer

Note:
- prefer a proper Windows API/provider path over localized CLI parsing when possible

### Phase 4 - IIS capability

Deliverables:
- app pools and sites snapshot
- site and app pool actions
- IIS-specific renderer

Why not first:
- more edge cases
- more product-specific behavior
- better to validate the platform pattern before the IIS domain

### Phase 5 - File tree capability

Deliverables:
- configured roots
- readonly exploration
- safe filtering and depth limits
- renderer optimized for tree browsing

### Phase 6 - Hardening

Deliverables:
- lease timeout handling
- retries and backoff
- better structured logs
- test coverage for action and snapshot flows
- operational docs for local and VM setup

---

## Testing strategy

Test at three levels:

### Contracts
- serialization stability
- schema version handling
- capability envelope compatibility

### Application
- action state transitions
- lease behavior
- approval rules
- snapshot acceptance rules

### Capability modules
- provider mapping
- target validation
- action execution rules

Use integration tests for:
- central API with SQLite
- agent-core to central flow

Use local smoke tests on Windows for:
- services
- scheduled tasks
- IIS

---

## Risks and mitigations

### Risk: over-generic capability abstraction too early

Mitigation:
- standardize only the lifecycle, not every payload shape

### Risk: central schema becomes capability-specific too soon

Mitigation:
- JSON payload storage plus minimal indexed metadata

### Risk: polling creates unnecessary load

Mitigation:
- separate heartbeat from action polling
- keep low action concurrency
- use backoff when idle or failing

### Risk: provider fragility on Windows

Mitigation:
- isolate OS access behind capability modules
- test each provider against real Windows environments

### Risk: file capability becomes dangerous

Mitigation:
- readonly scope in MVP
- explicit configured roots only
- no delete or write actions in first iteration

---

## Open decisions to validate before coding

These do not block architecture, but we should confirm them before implementation:
- .NET target version for the whole solution
- whether the web UI will be Blazor Server or another .NET web approach
- exact agent credential model after enrollment
- final scheduled task provider approach
- whether snapshots are published per capability or as one envelope containing many capability payloads

---

## Recommended next action

Start implementation from platform infrastructure, not from IIS.

First coding increment:
1. create the solution and projects
2. define contracts and capability abstractions
3. build central registration, heartbeat, and action queue
4. build agent core loops
5. validate a no-op action end to end
6. only then start the `services` capability
