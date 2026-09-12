# Writing a docs page

The guide explains what Hardened does. It does not explain C#, HTTP, or the parts of the .NET
libraries a reader already has. Read this before adding or editing a page under `guide/` or
`reference/`.

## What earns a paragraph

Write about what the build generates, what the pipeline does with it, and what the reader would
get wrong by guessing. Everything else is a table row or nothing.

A fact earns a paragraph if a competent C# developer could not predict it. These qualify:

- A declaration produces something at build time: a validator, a route, a binder, a converter.
- Two declarations are read by one generator, or one declaration is read by two.
- A diagnostic fires. Name the code. The message lives in `reference/diagnostics`.
- Two mechanisms answer the same input differently, and which one answers is not obvious. A route
  constraint answers 404 and a value constraint answers 400 on what looks like one rule.
- A declaration silently does nothing without a second one.
- A service is replaceable, and what the stock one does.

These do not:

- What a non-nullable reference type, an initializer, a settable property or a record is.
- What the C# compiler or `System.Text.Json` does on its own, except where Hardened changes it.
- What a thing is not, what it does not have, and what was considered and left out.
- That a fact is important, a trade-off, worth knowing, deliberate, or the same as before.

## Cut

Delete a sentence that frames, motivates, or announces rather than states. "The trade is worth
knowing", "It is worth saying that", "This matters because", "Note that".

Delete a sentence that restates the code block after it. The code shows it. One line before a
block says what to look at in it, and nothing else.

Delete the second statement of a point. The first one was the page's.

## How a sentence reads

One idea per sentence. Lead with the result and put the detail after it.

Name the identifier: the type, the attribute, the diagnostic code, the package. Do not invent a
role for it. `System.Text.Json`'s deserializer is not "the reader", and it does not know, ask,
or own anything.

Do not open a sentence with an abstract noun as its subject. "Absence is the one thing a validator
cannot see" is a sentence about a concept. "A validator never sees an omitted member" is a sentence
about the code.

No epigrams and no balanced clauses. "Presence is the reader's question; content is the
validator's" reads as a conclusion and carries no mechanism.

No em-dashes. The aside is a sentence or it is cut.

Plain words. A test fails; it does not explode.

## Headings

A heading names what the section answers, in the reader's words. "Which members are required", not
"Presence, and who checks it".

## Pages that read the way this asks

`guide/json`, `guide/routing`, `guide/testing`.
