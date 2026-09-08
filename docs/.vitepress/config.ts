import { defineConfig } from 'vitepress';

// Published under the repository name on GitHub Pages, so every absolute path needs it as a base.
//
// It was '/Hardened.Docs/' until the site moved into the repository it documents. That changes the
// published URL, and the old one is not redirected from here: Hardened.Docs has to keep a page that
// points at this one, because a repository cannot forward a path it no longer serves.
const base = '/Hardened.Framework/';

const repo = 'https://github.com/ipjohnson/Hardened.Framework';

// One sidebar for the guide and the cloud pages, so AWS, Google Cloud and Azure are the sections after
// Testing rather than separate trees.
const guide = [
  {
    text: 'Start here',
    items: [
      { text: 'Getting started', link: '/guide/getting-started' },
      { text: 'Project templates', link: '/guide/project-templates' },
      { text: 'Modules', link: '/guide/modules' },
      { text: 'Registering services', link: '/guide/services' },
    ],
  },
  {
    text: 'Application',
    items: [
      { text: 'Configuration', link: '/guide/configuration' },
      { text: 'Environments', link: '/guide/environments' },
    ],
  },
  {
    text: 'Handlers',
    items: [
      { text: 'Routing', link: '/guide/routing' },
      { text: 'Triggers', link: '/guide/triggers' },
      { text: 'Parameter binding', link: '/guide/parameter-binding' },
      { text: 'Declared responses', link: '/guide/responses' },
      { text: 'Validation', link: '/guide/validation' },
      { text: 'The execution pipeline', link: '/guide/execution-pipeline' },
    ],
  },
  {
    text: 'Contracts',
    items: [
      { text: 'Generating from OpenAPI', link: '/guide/openapi' },
      { text: 'Generating from Smithy', link: '/guide/smithy' },
      { text: 'The OpenAPI document', link: '/guide/openapi-document' },
      { text: 'Generated clients', link: '/guide/clients' },
    ],
  },
  {
    text: 'Security',
    items: [
      { text: 'Authentication', link: '/guide/authentication' },
      { text: 'Authorization', link: '/guide/authorization' },
    ],
  },
  {
    text: 'Serialization',
    items: [
      { text: 'Content negotiation', link: '/guide/content-negotiation' },
      { text: 'JSON serialization', link: '/guide/json' },
      { text: 'Streaming responses', link: '/guide/streaming' },
      { text: 'Views', link: '/guide/templates' },
    ],
  },
  {
    text: 'Performance and limits',
    items: [
      { text: 'Response caching', link: '/guide/response-caching' },
      { text: 'Conditional requests', link: '/guide/conditional-requests' },
      { text: 'Compression', link: '/guide/compression' },
      { text: 'Rate limiting', link: '/guide/rate-limiting' },
      { text: 'Request timeouts', link: '/guide/request-timeouts' },
    ],
  },
  {
    text: 'Testing',
    items: [
      { text: 'Writing a test', link: '/guide/testing' },
      { text: 'Sending requests', link: '/guide/testing-web' },
      { text: 'Substituting services', link: '/guide/testing-mocks' },
      { text: 'Credentials', link: '/guide/testing-credentials' },
      { text: 'Typed clients', link: '/guide/testing-clients' },
      { text: 'Asserting a response', link: '/guide/testing-responses' },
      { text: 'Test hosts', link: '/guide/testing-hosts' },
      { text: 'Steps and retries', link: '/guide/testing-steps' },
      { text: 'Writing a test attribute', link: '/guide/testing-attributes' },
    ],
  },
  {
    text: 'AWS',
    items: [
      { text: 'Overview', link: '/aws/' },
      { text: 'API Gateway', link: '/aws/lambda-web' },
      { text: 'Lambda functions', link: '/aws/lambda-function' },
      { text: 'Queues and topics', link: '/aws/sqs' },
      { text: 'Streams and change feeds', link: '/aws/ddb-streams' },
      { text: 'DynamoDB client', link: '/aws/dynamodb' },
      { text: 'Testing AWS handlers', link: '/aws/testing' },
    ],
  },
  {
    text: 'Google Cloud',
    items: [
      { text: 'Overview', link: '/gcp/' },
      { text: 'Web services', link: '/gcp/web' },
      { text: 'Queues', link: '/gcp/queue' },
      { text: 'Topics', link: '/gcp/topic' },
      { text: 'Timers', link: '/gcp/timer' },
      { text: 'Invocations', link: '/gcp/invoke' },
      { text: 'Blobs', link: '/gcp/blob' },
      { text: 'Changes', link: '/gcp/change' },
      { text: 'Events', link: '/gcp/event' },
      { text: 'Testing Cloud Run handlers', link: '/gcp/testing' },
    ],
  },
  {
    text: 'Azure',
    items: [
      { text: 'Overview', link: '/azure/' },
      { text: 'Web applications', link: '/azure/web' },
      { text: 'Queues', link: '/azure/queue' },
      { text: 'Topics', link: '/azure/topic' },
      { text: 'Timers', link: '/azure/timer' },
      { text: 'Streams', link: '/azure/stream' },
      { text: 'Changes', link: '/azure/change' },
      { text: 'Blobs', link: '/azure/blob' },
      { text: 'Events', link: '/azure/event' },
      { text: 'Testing Azure handlers', link: '/azure/testing' },
    ],
  },
];

export default defineConfig({
  title: 'Hardened',
  description:
    'A compile-time .NET framework for web APIs, AWS Lambda, Google Cloud Run and Azure Functions. Routing, dependency injection, ' +
    'configuration and parameter binding are generated during the build — nothing reflects, ' +
    'nothing scans at startup.',
  base,
  lang: 'en-GB',
  cleanUrls: true,

  // docs/ holds more than the site. design/ is the maintainer notes that used to live in each
  // repository's own docs folder, and README.md tells a contributor how to build this. Neither is
  // a page, and without this every one of them would be published as an unlinked orphan.
  srcExclude: ['design/**', 'README.md'],

  // A broken internal link fails the build rather than shipping.
  ignoreDeadLinks: false,

  markdown: {
    // Shiki ships no Smithy grammar. Kotlin's is close enough for annotations, braces and strings,
    // and the fence still reads `smithy`.
    languageAlias: { smithy: 'kotlin' },
  },

  head: [
    ['link', { rel: 'icon', href: `${base}favicon.svg`, type: 'image/svg+xml' }],
    // Gunmetal from the Plate mark's tile, so browser chrome matches the favicon.
    ['meta', { name: 'theme-color', content: '#1B242E' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Hardened' }],
    [
      'meta',
      {
        property: 'og:description',
        content: 'A compile-time .NET framework for web APIs, AWS Lambda, Google Cloud Run and Azure Functions.',
      },
    ],
  ],

  themeConfig: {
    siteTitle: 'Hardened',
    // The Plate mark, light and dark variants. themeConfig paths get `base` applied by the
    // theme, unlike the head entries above.
    logo: { light: '/hardened-mark.svg', dark: '/hardened-mark-dark.svg' },

    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '/guide/' },
      { text: 'AWS', link: '/aws/', activeMatch: '/aws/' },
      { text: 'Google Cloud', link: '/gcp/', activeMatch: '/gcp/' },
      { text: 'Azure', link: '/azure/', activeMatch: '/azure/' },
      { text: 'Reference', link: '/reference/attributes', activeMatch: '/reference/' },
    ],

    sidebar: {
      '/guide/': guide,
      '/aws/': guide,
      '/gcp/': guide,
      '/azure/': guide,
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Attributes', link: '/reference/attributes' },
            { text: 'Diagnostics', link: '/reference/diagnostics' },
            { text: 'Packages', link: '/reference/packages' },
            { text: 'Repository', link: '/reference/repository' },
          ],
        },
      ],
    },

    socialLinks: [{ icon: 'github', link: repo }],

    search: { provider: 'local' },

    editLink: {
      pattern: `${repo}/edit/main/docs/:path`,
      text: 'Edit this page on GitHub',
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Ian Johnson',
    },

    outline: [2, 3],
  },
});
