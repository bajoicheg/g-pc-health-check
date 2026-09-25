from pathlib import Path
import json,unittest
ROOT=Path(__file__).resolve().parents[1]
class Tests(unittest.TestCase):
 def test_version_is_27(self):
  self.assertEqual((ROOT/"VERSION").read_text().strip(),"2.7.0")
  self.assertEqual(json.loads((ROOT/"manifest.json").read_text())["version"],"2.7.0")
 def test_core_names_27_release_contracts(self):
  t=(ROOT/"SKILL.md").read_text().lower()
  for term in ("canonical source","independent release","consumer locks","candidate runtime","package git tree","self-hosting"): self.assertIn(term,t)
 def test_reference_separates_evidence_classes(self):
  t=(ROOT/"references"/"canonical-source-and-release.md").read_text().lower()
  for term in ("bootstrap","package","compatibility","fault-injection","three distinct consumers"): self.assertIn(term,t)
 def test_watchdog_treats_version_equality_as_insufficient(self):
  t=(ROOT/"templates"/"watchdog-prompt.md").read_text().lower()
  self.assertIn("version equality alone is not convergence",t);self.assertIn("vendored core",t)
 def test_pressure_suite_covers_release_and_drift_fail_closed(self):
  t=(ROOT/"tests"/"pressure-scenarios.md").read_text().lower()
  for term in ("candidate validates itself","consumer version matches but package tree differs","migration crosses active owner"): self.assertIn(term,t)
if __name__=="__main__": unittest.main()
