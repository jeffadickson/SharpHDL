(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (bvnot (bvnot (ite (bvult #xffffffff input_0) #b1 #b0)))))
  (not (= #b1 a!1))))

(check-sat)
(get-model)
