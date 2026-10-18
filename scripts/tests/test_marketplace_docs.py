"""Offline contracts for runnable release examples and browser publication boundaries."""
from pathlib import Path
import re
import unittest
import textwrap

import yaml

ROOT = Path(__file__).resolve().parents[2]


def command_blocks(text):
    return [textwrap.dedent(block) for block in re.findall(
        r'^[ \t]*```(?:bash|sh|shell|env|powershell|cmd)[ \t]*\n(.*?)^[ \t]*```', text, re.M | re.S)]


def unsafe_examples(text):
    errors = []
    for block in command_blocks(text):
        for line in block.splitlines():
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            if re.search(r'PUBLISH_MARKETPLACES\s*=\s*[\"\']?true\b', line, re.I):
                errors.append('paused marketplace enabled')
            if re.search(r'--publish-marketplaces\b|(?:^|\s)(?:vsce|VsixPublisher(?:\.exe)?)\s+publish\b|(?:-f\s+)?publish=true\b', line, re.I):
                errors.append('publication command in preparation examples')
            if re.search(r'^DG_RELEASE_GITHUB_TOKEN\s*=\s*\S+', line):
                errors.append('token-file example')
    return errors


class MarketplaceDocsTests(unittest.TestCase):
    def test_release_examples_do_not_enable_paused_publication_or_store_tokens(self):
        for relative in ('docs/operations/release_guide.md', 'docs/operations/release_guide.vi.md',
                         'docs/marketplace-publishing.md', 'docs/marketplace-publishing.vi.md'):
            with self.subTest(path=relative):
                self.assertEqual([], unsafe_examples((ROOT / relative).read_text()))

    def test_example_guard_rejects_old_instructions_and_quoted_variants(self):
        for command in ('PUBLISH_MARKETPLACES=true', 'PUBLISH_MARKETPLACES="true"',
                        'bash dg-release --publish-marketplaces', 'gh workflow run marketplace.yml -f publish=true',
                        'vsce publish', 'VsixPublisher.exe publish', 'DG_RELEASE_GITHUB_TOKEN=github_pat_example'):
            with self.subTest(command=command):
                self.assertTrue(unsafe_examples('```bash\n' + command + '\n```\n'))
        self.assertEqual([], unsafe_examples('```env\nPUBLISH_MARKETPLACES=false\nDRY_RUN=true\n```\n'))

    def test_both_browser_guides_keep_product_specific_publication_boundaries(self):
        for name in ('marketplace-publishing.md', 'marketplace-publishing.vi.md'):
            text = (ROOT / 'docs' / name).read_text()
            with self.subTest(guide=name):
                for marker in ('New extension', 'Visual Studio Code', 'Save & Upload', 'Make Public',
                               '18.10.2', 'SHA-256', '0.3.0', '0.4.0', 'thanhnt-sm',
                               'Publisher Agreement', 'https://marketplace.visualstudio.com/manage',
                               'https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension',
                               'https://code.visualstudio.com/api/working-with-extensions/publishing-extension'):
                    self.assertIn(marker, text)
                if name.endswith('.vi.md'):
                    self.assertIn('Không cần PAT', text)
                    self.assertIn('không giả định', text)
                else:
                    self.assertIn('No PAT is required', text)
                    self.assertIn('do not assume', text)

    def test_docs_match_paused_marketplace_and_mutable_nightly_workflows(self):
        marketplace = yaml.safe_load((ROOT / '.github/workflows/marketplace.yml').read_text())
        self.assertEqual({'test', 'vscode-package', 'visual-studio-package'}, set(marketplace['jobs']))
        triggers = marketplace.get('on', marketplace.get(True))
        self.assertNotIn('publish', triggers['workflow_dispatch']['inputs'])
        nightly = yaml.safe_load((ROOT / '.github/workflows/installers.yml').read_text())
        triggers = nightly.get('on', nightly.get(True))
        self.assertIn('main', triggers['push']['branches'])
        self.assertIn('**/*.md', triggers['push']['paths-ignore'])
        self.assertTrue(triggers['workflow_dispatch']['inputs']['publish_prerelease']['default'])
        self.assertEqual("github.event_name == 'push' || inputs.publish_prerelease", nightly['jobs']['publish']['if'])
        run = '\n'.join(step.get('run', '') for step in nightly['jobs']['publish']['steps'])
        self.assertIn('gh release delete "$TAG" --yes --cleanup-tag', run)
        self.assertIn('gh api -X DELETE', run)
        for name in ('marketplace-publishing.md', 'marketplace-publishing.vi.md'):
            text = (ROOT / 'docs' / name).read_text()
            for marker in ('installers.yml', 'paths-ignore', 'publish_prerelease=false',
                           'nightly', 'tag', 'build_release.yml'):
                self.assertIn(marker, text)

    def test_release_guides_describe_supported_token_file_without_recommending_it(self):
        for name, old_claim in (('release_guide.md', 'never reads a secret file'),
                                ('release_guide.vi.md', 'không đọc file chứa secret')):
            text = (ROOT / 'docs/operations' / name).read_text()
            self.assertNotIn(old_claim, text)
            self.assertIn('DG_RELEASE_GITHUB_TOKEN', text)
            self.assertIn('.release.env', text)
            self.assertIn('marketplace-publishing', text)
            blocks = command_blocks(text)
            configs = [block for block in blocks if re.search(r'^RELEASE_TAG=', block, re.M)]
            self.assertTrue(configs)
            for config in configs:
                self.assertRegex(config, r'(?m)^PUBLISH_MARKETPLACES=false$')
                self.assertRegex(config, r'(?m)^DRY_RUN=true$')


if __name__ == '__main__':
    unittest.main()
