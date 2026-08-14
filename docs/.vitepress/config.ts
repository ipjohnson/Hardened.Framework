import { defineConfig } from 'vitepress';

// Published to https://ipjohnson.github.io/Hardened.Docs/, so every absolute path needs the
// repository name as a base. Getting this wrong is the classic Pages failure: the site builds, the
// landing page loads, and every asset and internal link 404s.
const base = '/Hardened.Docs/';

// The ecosystem is three repositories. They are linked from the nav, the AWS overview and the
// repositories reference page, because a reader who arrives at the DynamoDB page wants the source
// for it without going back to a landing page to find out where it lives.
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

  // A broken internal link should fail the build rather than ship. The pages cross-reference
  // heavily and a rename would otherwise rot links silently.
  ignoreDeadLinks: false,

  head: [
    ['link', { rel: 'icon', href: `${base}favicon.svg`, type: 'image/svg+xml' }],
    ['meta', { name: 'theme-color', content: '#5b8def' }],
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
            { text: 'Installation', link: '/guide/getting-started' },
            { text: 'Modules', link: '/guide/modules' },
            { text: 'Registering services', link: '/guide/services' },
          ],
        },
        {
          text: 'Applications',
          items: [
            { text: 'Configuration', link: '/guide/configuration' },
            { text: 'Environments', link: '/guide/environments' },
            { text: 'Console commands', link: '/guide/console' },
          ],
        },
        {
          text: 'Handling requests',
          items: [
            { text: 'Routing', link: '/guide/routing' },
            { text: 'Parameter binding', link: '/guide/parameter-binding' },
            { text: 'The execution pipeline', link: '/guide/execution-pipeline' },
            { text: 'Generating from OpenAPI', link: '/guide/openapi' },
            { text: 'Content negotiation', link: '/guide/content-negotiation' },
            { text: 'Templates', link: '/guide/templates' },
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
