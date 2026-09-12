# Docs style guide

The site is reference documentation for a working .NET developer. It describes what Hardened does.
It does not teach C#, teach HTTP, or argue that a design is correct.

Two roles follow this file. A writer produces a page against it. An editor reviews the page against
it and reports violations by rule number, with the sentence quoted. Neither role rewrites the other's
structure: the writer owns the page, the editor owns the list of rules broken.

## 1. What earns prose

A fact earns a sentence if a competent C# developer could not predict it. Everything else is a table
row, a code comment, or nothing.

These earn prose:

- A declaration produces something at build time: a route, a validator, a binder, a formatter.
- One declaration is read by two generators, or two declarations are read by one.
- A diagnostic fires. Name the code and what to change. The message itself lives in
  `reference/diagnostics`.
- A declaration does nothing without a second one.
- A default differs from the platform default, or from what another framework does.
- A service is replaceable. Name the interface, the stock implementation, and the registration.

These do not:

- What a record, a constructor, a non-nullable reference type, an initializer or a property is.
- What the C# compiler, `System.Text.Json` or ASP.NET Core does on its own. State it only where
  Hardened changes it, and then state the difference, not the background.
- Behaviour that matches what every other server does. A path that does not match a route is a 404
  everywhere. It needs no paragraph here.
- What a feature does not do, what was removed, or what was considered and rejected.

## 2. Rules

An editor cites these by number.

**2.1 No posed question and answer.** Do not set up a question in order to answer it. Do not split a
behaviour into a rhetorical pair. "Presence is the reader's question; content is the validator's" is
a violation. Write what is checked, and where.

**2.2 No defending a decision.** The page states behaviour. It does not argue that the behaviour is
right. Cut "deliberately", "that is the design rather than a gap", "and will not", "not for want of",
"which is exactly the case it should decide". A constraint a reader must work within is a fact: "an
index is never assigned; `HOAT033` names the member and the next free index". Why it is never
assigned belongs in `design/`.

**2.3 No epigrams or balanced clauses.** No aphorism, no antithesis, no sentence built for its shape.

**2.4 Name the mechanism.** Use the type, attribute, property, diagnostic code or package name. A
mechanism does not ask, know, own, decide for itself, or have a question.
`System.Text.Json`'s deserializer is not "the reader".

**2.5 No abstract noun as a sentence subject.** Not "Absence is the one thing a validator cannot
see". Write "a validator never sees an omitted member", or delete it.

**2.6 No sentence that restates the code below it.** One line introduces a block by naming what to
look at. The block shows the rest.

**2.7 No framing, motivating or announcing.** Cut "Note that", "It is worth saying", "The trade is
worth knowing", "This matters because", "Deserialization is simpler".

**2.8 No selling.** No "worth having", "reads better", "it is yours to edit", "which is what a client
should be anyway". The reader decides what is worth having.

**2.9 No em-dashes.** An aside is a sentence, or it is cut.

**2.10 Headings name their subject.** A noun phrase a reader would scan for: "Request bodies",
"Required members", "Registering a serializer". Not "Presence, and who checks it", "The request side
is not negotiated", "Rules the vocabulary cannot express".

**2.11 Plain words, present tense, active voice.** A test fails. It does not explode.

**2.12 One idea per sentence, result first.** The detail goes after the result, in its own sentence.

## 3. Page shape

1. An H1 naming the feature.
2. One paragraph: what it does, and what the reader writes to get it. No preamble.
3. A code block showing the smallest complete example, and the response it produces where there is
   one.
4. Sections in the order a reader meets them: declaring it, what the build generates, what the
   runtime does, extending it, limits.
5. A `## Next` list of two to four links, each with four to eight words on what is there.

A table is the default for anything enumerable: options and their values, attributes and what they
check, facets and what they generate, inputs and their status codes. Prose around a table repeats the
table.

## 4. Code blocks

Complete enough to compile. The first block on a page carries its `using` lines, and a later block
adds one only for a namespace the page has not shown yet. Repeating the same `using` in every block
is the padding this guide exists to remove.

A comment in a block states the result, not the intent: `// {} is a 400`, not `// this is required`.

## 5. What the editor returns

A list. Each entry is the rule number, the sentence quoted, and what it should be instead or that it
should be cut. No rewritten page, no praise, and no findings outside these rules.
