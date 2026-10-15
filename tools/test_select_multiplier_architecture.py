import unittest
from select_multiplier_architecture import select_architecture

REPORT={"device":"example-fpga","results":[
    {"architecture":"Iterative","lut":80,"dsp":0,"fmax_mhz":150,"latency":17,"initiation_interval":18},
    {"architecture":"Operator","lut":200,"dsp":1,"fmax_mhz":220,"latency":2,"initiation_interval":1},
    {"architecture":"WallaceTree","lut":160,"dsp":0,"fmax_mhz":260,"latency":3,"initiation_interval":1}
]}

class SelectionTests(unittest.TestCase):
    def test_area_throughput_latency_are_distinct(self):
        self.assertEqual("Iterative",select_architecture(REPORT,"area")["architecture"])
        self.assertEqual("WallaceTree",select_architecture(REPORT,"throughput")["architecture"])
        self.assertEqual("Operator",select_architecture(REPORT,"latency")["architecture"])

    def test_missing_data_is_not_zero(self):
        incomplete={"device":"example-fpga","results":[{"architecture":"Incomplete","lut":0}]}
        with self.assertRaises(ValueError):
            select_architecture(incomplete,"area")

    def test_target_clock_filters_unqualified_design(self):
        self.assertEqual("WallaceTree",select_architecture(REPORT,"area",target_mhz=250)["architecture"])
        with self.assertRaises(ValueError):
            select_architecture(REPORT,"area",target_mhz=500)

if __name__=="__main__":
    unittest.main()
