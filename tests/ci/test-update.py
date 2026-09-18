#!/usr/bin/env python3
"""Offline scheduling and publication-state contracts."""
import base64
from datetime import datetime, timedelta, timezone
import importlib.util
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('update', ROOT / 'scripts/automation/update.py')
update = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(update)
SHA = 'a' * 40
BLOB = 'b' * 40
NOW = datetime(2026, 9, 18, 12, tzinfo=timezone.utc)


def metadata(manifest='123'):
    return {'formatVersion': 2, 'contentSha256': 'c' * 64, 'extractorCommit': SHA,
            'steam': {'appId': 1808500, 'depotId': 1808501, 'manifestId': manifest}}


def release(version='1.0.0', commit=SHA):
    return {'ref': f'refs/tags/exfil-v{version}', 'object': {'type': 'commit', 'sha': commit}}


def job(conclusion='failure', age=30, name=update.EXTRACTION_JOB):
    return {'name': name, 'conclusion': conclusion,
            'completed_at': (NOW - timedelta(minutes=age)).isoformat()}


def run(conclusion='failure', status='completed'):
    return {'id': 7, 'status': status, 'conclusion': conclusion}


class UpdateTests(unittest.TestCase):
    def test_steam_version_supports_public_feed_shapes_and_uint64(self):
        for value in ['1', '18446744073709551615', {'gid': '123'}]:
            payload = {'data': {'1808500': {'depots': {'1808501': {'manifests': {'public': value}}}}}}
            self.assertEqual(update.steam_manifest(payload), value['gid'] if isinstance(value, dict) else value)
        for value in [None, 1, '', '0', '01', '18446744073709551616', '123\n', '$(echo bad)']:
            with self.subTest(value=value), self.assertRaises(ValueError):
                update.manifest_id(value)

    def test_no_publication_marker_for_legacy_but_reject_unknown_new_formats(self):
        self.assertIsNone(update.published_manifest({'version': 'legacy'}))
        self.assertEqual(update.published_manifest(metadata()), '123')
        for field, value in [('formatVersion', 3), ('extractorCommit', 'main'), ('contentSha256', 'bad')]:
            data = metadata()
            data[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                update.published_manifest(data)
        data = metadata()
        data['steam']['depotId'] = 1
        with self.assertRaises(ValueError):
            update.published_manifest(data)

    def test_release_selection_is_numeric_exact_and_never_accepts_branch_or_annotated_tag(self):
        refs = [release('1.9.0'), release('1.10.0', BLOB), release('01.99.0'),
                {'ref': 'refs/heads/main', 'object': {'type': 'commit', 'sha': SHA}}]
        self.assertEqual(update.source_release(refs), (BLOB, 'exfil-v1.10.0'))
        invalid = release('2.0.0')
        invalid['object']['type'] = 'tag'
        with self.assertRaises(ValueError):
            update.source_release(refs + [invalid])
        with self.assertRaises(ValueError):
            update.source_release([])

    def test_no_change_does_not_resolve_source_or_query_history(self):
        with patch.object(update, 'public_steam_manifest', return_value='123'), \
             patch.object(update, 'publication', return_value=('123', BLOB)), \
             patch.object(update, 'github') as api, patch.object(update, 'recent_runs') as runs:
            self.assertFalse(update.plan(False, True, 'schedule')['needed'])
            api.assert_not_called()
            runs.assert_not_called()

    def test_activation_gate_skips_network_but_allows_manual_export(self):
        with patch.object(update, 'public_steam_manifest') as steam:
            self.assertFalse(update.plan(False, False, 'schedule')['needed'])
            steam.assert_not_called()
        self.assertTrue(self.plan('123', force=True, enabled=False)['needed'])

    def plan(self, current, *, force=False, enabled=True, ancestry='ahead'):
        responses = {'git/matching-refs/tags/exfil-v': [release()], f'compare/{SHA}...main': {'status': ancestry}}
        with patch.object(update, 'public_steam_manifest', return_value='123'), \
             patch.object(update, 'publication', return_value=(current, BLOB)), \
             patch.object(update, 'recent_runs', return_value=[]), \
             patch.object(update, 'github', side_effect=responses.__getitem__):
            return update.plan(force, enabled, 'workflow_dispatch')

    def test_unpublished_release_and_manual_force_pin_source_and_previous_metadata(self):
        result = self.plan('122')
        self.assertEqual(result['extractor_commit'], SHA)
        self.assertEqual(result['manifest_id'], '123')
        self.assertEqual(result['expected_metadata_blob'], BLOB)
        self.assertTrue(self.plan('123', force=True)['needed'])
        with self.assertRaises(ValueError):
            self.plan('122', ancestry='diverged')

    def test_only_real_failed_attempts_start_cooldown(self):
        self.assertIsNone(update.retry_blocked([run()], lambda _: [job(name='plan')], NOW))
        self.assertIsNone(update.retry_blocked([run()], lambda _: [job(conclusion='skipped')], NOW))
        self.assertIsNone(update.retry_blocked([run('success')], lambda _: [job()], NOW))
        self.assertIsNone(update.retry_blocked([run()], lambda _: [job(age=61)], NOW))
        for outcome in ['failure', 'cancelled', 'timed_out']:
            with self.subTest(outcome=outcome):
                self.assertIn('cooldown', update.retry_blocked([run(outcome)], lambda _: [job()], NOW))
        # Publisher failure after successful extraction also avoids immediately repeating the expensive job.
        self.assertIn('cooldown', update.retry_blocked([run()], lambda _: [job('success')], NOW))

    def test_active_run_blocks_but_queued_polls_do_not_deadlock_current_run(self):
        for status in ['in_progress', 'waiting', 'pending', 'requested']:
            self.assertIn('active', update.retry_blocked([run(status=status)], lambda _: [], NOW))
        self.assertIsNone(update.retry_blocked([run(status='queued')], lambda _: [], NOW))

    def test_state_is_read_from_one_immutable_tree(self):
        paths = []
        responses = {
            'git/ref/heads/data': {'object': {'sha': SHA}},
            'git/trees/' + SHA: {'tree': [{'path': 'metadata.json', 'type': 'blob', 'mode': '100644', 'sha': BLOB}]},
            'git/blobs/' + BLOB: {'encoding': 'base64', 'content': base64.b64encode(json.dumps(metadata()).encode()).decode()},
        }
        def api(path):
            paths.append(path)
            return responses[path]
        with patch.object(update, 'github', side_effect=api):
            self.assertEqual(update.publication(), ('123', BLOB))
        self.assertEqual(paths, list(responses))
        responses['git/trees/' + SHA]['tree'][0]['mode'] = '120000'
        with patch.object(update, 'github', side_effect=api), self.assertRaises(ValueError):
            update.publication()
        responses['git/trees/' + SHA]['tree'] = []
        with patch.object(update, 'github', side_effect=api):
            self.assertEqual(update.publication(), (None, 'missing'))

    def test_missing_data_branch_never_falls_back_to_source_main(self):
        with patch.object(update, 'github', side_effect=RuntimeError('Missing data branch')) as api:
            with self.assertRaises(RuntimeError):
                update.publication()
            api.assert_called_once_with('git/ref/heads/data')

    def test_publication_rejects_new_steam_version_and_defers_git_state_to_publisher(self):
        with patch.object(update, 'public_steam_manifest', return_value='124'), self.assertRaises(ValueError):
            update.verify_current('123')
        with patch.object(update, 'public_steam_manifest', return_value='123'), \
             patch.object(update, 'publication') as publication:
            update.verify_current('123')
            publication.assert_not_called()

    def test_retry_history_does_not_count_the_current_run(self):
        data = [{'id': 7, 'status': 'in_progress', 'updated_at': NOW.isoformat(), 'created_at': NOW.isoformat()},
                {'id': 6, 'status': 'completed', 'updated_at': NOW.isoformat(), 'created_at': NOW.isoformat()}]
        with patch.dict(os.environ, {'GITHUB_RUN_ID': '7'}), \
             patch.object(update, 'github', side_effect=[{'workflow_runs': data}, {'workflow_runs': []}]):
            self.assertEqual([item['id'] for item in update.recent_runs(NOW)], [6])


if __name__ == '__main__':
    unittest.main()
