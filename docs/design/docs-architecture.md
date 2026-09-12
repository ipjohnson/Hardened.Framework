# Docs architecture

What the site covers, tab by tab, and what each page owns. A page states its own subject and links
rather than restating a neighbour's. Written alongside [the style guide](./writing-style.md), which
governs how a page reads.

68 pages today: 37 under `guide/`, 4 under `reference/`, 7 AWS, 10 Google Cloud, 10 Azure.

## The five tabs

| Tab | Answers | Reader |
|---|---|---|
| Guide | How do I build this with Hardened | Has a project, wants a feature |
| AWS, Google Cloud, Azure | What changes on this cloud | Has a project, picked a cloud |
| Reference | What is the exact name, code, or package | Knows what they want, needs the spelling |

A cloud tab holds only what differs on that cloud. The handler, the pipeline and the contract are
the same everywhere and belong in the guide. A cloud page that explains routing again is wrong.

The Reference tab is generated or tabular: every attribute, every diagnostic, every package. No
prose beyond a line per entry.

## Guide sections

Nine sections, in the order a reader meets them. The section is the unit a reader scans, so a page
belongs to the section matching the question it answers, not the subsystem that implements it.

### Start here

`getting-started`, `project-templates`, `modules`, `services`.

Everything needed before a first successful build. A reader who opens only this section has a running
application with tests. `getting-started` owns the assembled-by-hand path and nothing else;
`project-templates` owns the template options.

### Application

`configuration`, `environments`.

What the application is, outside any one request.

### Handlers

`routing`, `triggers`, `parameter-binding`, `responses`, `validation`, `execution-pipeline`.

One request, from the route to the response. Each page owns one stage:

| Page | Owns |
|---|---|
| `routing` | The verb attributes, path tokens, route constraints, `[BasePath]`, how a route is matched |
| `triggers` | A handler reached by something other than HTTP: queue, topic, timer, change, stream, blob |
| `parameter-binding` | Where a parameter's value comes from, and what a binding failure answers |
| `responses` | Declaring statuses: `Response<T1..Tn>`, `[Throws<T>]`, unions, the built-in response records |
| `validation` | Constraint attributes, required members, the 400 envelope, replacing it |
| `execution-pipeline` | Filters, ordering, `chain.Next()`, the shipped positions |

### Contracts

`openapi`, `smithy`, `openapi-document`, `clients`.

`openapi` and `smithy` own generating an application from a contract. `openapi-document` owns the
document Hardened publishes. `clients` owns generating a client from it. The three are frequently
confused and each page says in its first paragraph which direction it describes.

### Security

`authentication`, `authorization`.

### Serialization

`content-negotiation`, `json`, `message-pack`, `streaming`, `templates`.

`content-negotiation` owns how a media type is chosen and `[Produces]`. Every other page in the
section owns one representation and links back for the negotiation rules rather than restating them.

### Performance and limits

`response-caching`, `conditional-requests`, `compression`, `rate-limiting`, `request-timeouts`.

Each is a filter with an attribute. Same page shape across all five: what to declare, where it sits
in the pipeline, what it answers when it refuses.

### Testing

Nine pages: `testing`, `testing-web`, `testing-mocks`, `testing-credentials`, `testing-clients`,
`testing-responses`, `testing-hosts`, `testing-steps`, `testing-attributes`.

`testing` owns the first test and the three assembly attributes. The rest each own one tool, and none
of them reintroduces the wiring.

## Page budget

A guide page states one subsystem. Over roughly 250 lines it is either two pages or padded, and
padded is the usual answer. The longest pages today are `smithy` at 462, `openapi` at 391 and
`responses` at 346.

Counted as prose words rather than lines, because tables and code blocks are the parts that earn
their length. `validation` carried 1328 prose words for 10 sections.

## Rewrite order

Page by page, worst first, each one written from a fact list extracted from the implementation, the
test assertions and the diagnostic messages. Not edited from the existing page: editing inside the
old paragraphs preserves the padding.

| Order | Page | Why first |
|---|---|---|
| 1 | `validation` | Already diagnosed. The required-member section is the worst case in the site |
| 2 | `message-pack` | Newest, longest rationale sections, whole paragraphs defending decisions |
| 3 | `content-negotiation` | Dense with real behaviour, buried under posed questions |
| 4 | `smithy`, `openapi` | Longest pages, and the two most likely to overlap each other |
| 5 | everything else | |

Facts do not come from XML doc comments in the source. Those carry the same voice the pages do:
`RequiredMemberPresenceTests` has "presence is the reader's question, content is the validator's" in a
`<summary>`, and `SerializationLocatorService` explains negotiation the same way.
