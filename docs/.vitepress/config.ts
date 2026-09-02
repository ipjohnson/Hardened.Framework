import { defineConfig } from 'vitepress';

// Published under the repository name on GitHub Pages, so every absolute path needs it as a base.
const base = '/Hardened.Docs/';

const repos = {
  framework: 'https://github.com/ipjohnson/Hardened.Framework',
  amz: 'https://github.com/ipjohnson/Hardened.Amz',
  docs: 'https://github.com/ipjohnson/Hardened.Docs',
};

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
      '/guide/': [
        {
          text: 'Getting started',
          items: [
            { text: 'Getting started', link: '/guide/getting-started' },
            { text: 'Project templates', link: '/guide/project-templates' },
            { text: 'Modules', link: '/guide/modules' },
            { text: 'Registering services', link: '/guide/services' },
          ],
        },
        {
          text: 'Applications',
          items: [
            { text: 'Configuration', link: '/guide/configuration' },
            { text: 'Environments', link: '/guide/environments' },
          ],
        },
        {
          text: 'Handling requests',
          items: [
            { text: 'Routing', link: '/guide/routing' },
            { text: 'Parameter binding', link: '/guide/parameter-binding' },
            { text: 'Validation', link: '/guide/validation' },
            { text: 'Authorization', link: '/guide/authorization' },
            { text: 'The execution pipeline', link: '/guide/execution-pipeline' },
            { text: 'Generating from OpenAPI', link: '/guide/openapi' },
            { text: 'Generating from Smithy', link: '/guide/smithy' },
            { text: 'The OpenAPI document', link: '/guide/openapi-document' },
            { text: 'Declared responses', link: '/guide/responses' },
            { text: 'Content negotiation', link: '/guide/content-negotiation' },
            { text: 'JSON serialization', link: '/guide/json' },
            { text: 'Streaming responses', link: '/guide/streaming' },
            { text: 'Views', link: '/guide/templates' },
          ],
        },
        {
          text: 'Testing',
          items: [
            { text: 'Writing a test', link: '/guide/testing' },
            { text: 'Testing web handlers', link: '/guide/testing-web' },
          ],
        },
      ],
      '/aws/': [
        {
          text: 'AWS',
          items: [
            { text: 'Overview', link: '/aws/' },
            { text: 'Lambda functions', link: '/aws/lambda-function' },
            { text: 'API Gateway', link: '/aws/lambda-web' },
            { text: 'DynamoDB Streams', link: '/aws/ddb-streams' },
            { text: 'SQS', link: '/aws/sqs' },
          ],
        },
        {
          text: 'Clients and infrastructure',
          items: [
            { text: 'DynamoDB client', link: '/aws/dynamodb' },
            { text: 'CDK', link: '/aws/cdk' },
          ],
        },
        {
          text: 'Testing',
          items: [{ text: 'Testing AWS handlers', link: '/aws/testing' }],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Attributes', link: '/reference/attributes' },
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
