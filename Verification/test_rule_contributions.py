import copy
import unittest
from rule_contributions import mask, union, metrics, contribution, family, audit


class ContributionChecks(unittest.TestCase):
    def test_shared_hits_are_not_restored(self):
        c=contribution([mask([1,2])],[mask([2,3])],[mask([1,2,4,5,6,7])],'Red')
        self.assertEqual((c['UniqueKills'],c['UniqueWrong'],c['RestoredFull']),(1,1,0))
        self.assertAlmostEqual(c['ExcessWrong'],1-6/33)

    def test_unique_blue_hit_is_restored(self):
        c=contribution([mask([1,2])],[mask([2,3])],[mask([1])],'Blue')
        self.assertEqual(c['RestoredFull'],1)
        self.assertEqual(c['UniqueKills'],1)

    def test_size_matched_expectation(self):
        m=metrics([mask(range(1,9)),mask(range(1,17))],[mask([16]),mask([16])],'Blue')
        self.assertEqual(m['Full'],1)
        self.assertEqual(m['RandomExpectedFull'],.5)
        self.assertEqual(m['UnderMinimum'],1)
        red=metrics([mask(range(7,34))],[mask(range(1,7))],'Red')
        self.assertAlmostEqual(red['RandomExpectedFull'],1/1107568)

    def test_related_rules_are_one_family(self):
        a={'ruleId':'B-S-B-001','params':{'geometryVersion':1,'offsets':[0],'target':1}}
        b=copy.deepcopy(a); b['params']['target']=7
        self.assertEqual(family(a),family(b))
        self.assertEqual(family({'ruleId':'B-G-R-003','params':{}}),family({'ruleId':'B-G-R-003-HS','params':{}}))

    def test_pending_draw_ignored_and_no_duplicate_family_votes(self):
        def row(rid, target):
            return {'Rule':{'ruleId':rid,'name':rid,'ballType':'Blue','params':{'geometryVersion':1,'offsets':[0],'target':target}},
                'Enabled':True,'Accuracy':.94,'Triggers':50,'Outputs':[[2] for _ in range(501)]}
        data={'Through':500,'DataHash':'fixture','History':[{'Period':i,'Red':[1,2,3,4,5,6],'Blue':1} for i in range(500)],
            'Rules':[row('a',1),row('b',7)]}
        before=audit(data)
        self.assertEqual(before['Rules'][0]['Contribution']['UniqueKills'],0)
        self.assertEqual(before['Scenarios']['两个不同图形类型确认']['Blue']['AverageRemaining'],16)
        data['Rules'][0]['Outputs'][-1]=list(range(1,17))
        self.assertEqual(audit(data),before)


if __name__=='__main__': unittest.main()
