import { defineConfig } from 'vitepress';

// Published under the repository name on GitHub Pages, so every absolute path needs it as a base.
const base = '/Hardened.Docs/';

const repos = {
  framework: 'https://github.com/ipjohnson/Hardened.Framework',
  amz: 'https://github.com/ipjohnson/Hardened.Amz',
  docs: 'https://github.com/ipjohnson/Hardened.Docs',
};

// One sidebar for the guide and the AWS pages, so AWS is the section after Testing rather than a
// separate tree.
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
      { text: 'SQS', link: '/aws/sqs' },
      { text: 'DynamoDB Streams', link: '/aws/ddb-streams' },
      { text: 'DynamoDB client', link: '/aws/dynamodb' },
      { text: 'CDK', link: '/aws/cdk' },
      { text: 'Testing AWS handlers', link: '/aws/testing' },
    ],
  },
];

export default defineConfig({
  title: 'Hardened',
  description:
    'A compile-time .NET framework for web APIs and AWS Lambda. Routing, dependency injection, ' +
    'configuration and parameter binding are generated during the build — nothing reflects, ' +
    'nothing scans at startup.',
  base,
  lang: 'en-GB',
  cleanUrls: true,

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
        content: 'A compile-time .NET framework for web APIs and AWS Lambda.',
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
      { text: 'Reference', link: '/reference/attributes', activeMatch: '/reference/' },
      {
        text: 'Repositories',
        items: [
          { text: 'Hardened.Framework', link: repos.framework },
          { text: 'Hardened.Amz (AWS)', link: repos.amz },
          { text: 'Hardened.Docs', link: repos.docs },
        ],
      },
    ],

    sidebar: {
      '/guide/': guide,
      '/aws/': guide,
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Attributes', link: '/reference/attributes' },
            { text: 'Diagnostics', link: '/reference/diagnostics' },
            { text: 'Packages', link: '/reference/packages' },
            { text: 'Repositories', link: '/reference/repositories' },
          ],
        },
      ],
    },

    socialLinks: [{ icon: 'github', link: repos.framework }],

    search: { provider: 'local' },

    editLink: {
      pattern: 'https://github.com/ipjohnson/Hardened.Docs/edit/main/website/:path',
      text: 'Edit this page on GitHub',
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Ian Johnson',
    },

    outline: [2, 3],
  },
});
