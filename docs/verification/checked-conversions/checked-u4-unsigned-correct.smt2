(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (= #b1 (bvnot (ite (bvult #xffffffff input_0) #b1 #b0)))))
  (not a!1)))

(check-sat)
